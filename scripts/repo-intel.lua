#!/usr/bin/env lua
--[[
  repo-intel.lua - deterministic, local code-intelligence for this repository.

  Inspired by Aider's repository-map architecture: parse real syntax trees,
  extract definitions and references, rank symbols with PageRank, and print
  token-budgeted Markdown that an LLM can consume instead of re-reading files.

  Structural parsing is done with the official tree-sitter CLI and real
  grammars (C#, JavaScript/JSX, Python). The CLI emits an XML CST with exact
  row/column ranges and field names; this script parses that into an index.

  Dependencies (Lua 5.1 compatible):
    - dkjson      (LuaRocks: luarocks --lua-version=5.1 install dkjson)
    - luafilesystem (LuaRocks or distro package lua51-luafilesystem)
    - tree-sitter CLI (>= 0.25) + grammars cloned under .repo-intel/grammars

  Commands:
    lua scripts/repo-intel.lua setup
    lua scripts/repo-intel.lua update [--force]
    lua scripts/repo-intel.lua overview [--no-refresh]
    lua scripts/repo-intel.lua map [--budget 1024] [--no-refresh]
    lua scripts/repo-intel.lua symbol <name> [--no-refresh]
    lua scripts/repo-intel.lua deps <name-or-module> [--no-refresh]
    lua scripts/repo-intel.lua status
]]

local SCRIPT_VERSION = "1.0.0"
local CACHE_VERSION = 2
local DEFAULT_MAP_BUDGET = 1024
local OVERVIEW_BUDGET = 500
local DAMPING = 0.85
local PAGERANK_ITERATIONS = 40

-- ---------------------------------------------------------------------------
-- Dependencies
-- ---------------------------------------------------------------------------

local ok_lfs, lfs = pcall(require, "lfs")
local ok_json, json = pcall(require, "dkjson")

if not ok_lfs or not ok_json then
  io.stderr:write([[
repo-intel: missing Lua dependency.

Install on Void Linux:
  xbps-install -Sy lua51-devel lua51-luafilesystem luarocks-lua51 tree-sitter-cli
  luarocks --lua-version=5.1 install dkjson

Other platforms:
  luarocks --lua-version=5.1 install luafilesystem dkjson
  (plus a tree-sitter CLI >= 0.25, e.g. your package manager's tree-sitter-cli)

Then run: lua scripts/repo-intel.lua setup
]])
  os.exit(1)
end

-- ---------------------------------------------------------------------------
-- Small utilities
-- ---------------------------------------------------------------------------

local function norm_ws(s)
  return (s:gsub("%s+", " "):gsub("^%s+", ""):gsub("%s+$", ""))
end

local function truncate(s, n)
  if not s then return "" end
  if #s <= n then return s end
  return (s:sub(1, n - 1):gsub("%s+$", "")) .. "..."
end

local function trim(s)
  return (s:gsub("^%s+", ""):gsub("%s+$", ""))
end

local function compact_name(s)
  s = norm_ws(s)
  s = s:gsub("%s*%.%s*", ".")
  s = s:gsub("%s*<%s*", "<"):gsub("%s*>%s*", ">")
  s = s:gsub("%s*,%s*", ",")
  return s
end

local function clean_module_source(s)
  s = trim(s)
  local inner = s:match('^"(.*)"$') or s:match("^'(.*)'$") or s
  return (trim(inner):gsub("%s+", ""))
end


local function split_lines(s)
  local lines = {}
  s = s:gsub("\r\n", "\n"):gsub("\r", "\n")
  for line in (s .. "\n"):gmatch("(.-)\n") do
    lines[#lines + 1] = line
  end
  return lines
end

local function get_line(lines, idx)
  return lines[idx + 1] or ""
end

local function slice_range(lines, sr, sc, er, ec)
  if sr == er then
    return get_line(lines, sr):sub(sc + 1, ec)
  end
  local parts = { get_line(lines, sr):sub(sc + 1) }
  for r = sr + 1, er - 1 do
    parts[#parts + 1] = get_line(lines, r)
  end
  parts[#parts + 1] = get_line(lines, er):sub(1, ec)
  return table.concat(parts, " ")
end

local function read_file(path)
  local f = io.open(path, "rb")
  if not f then return nil, "cannot open " .. path end
  local data = f:read("*a")
  f:close()
  return data
end

local function write_file(path, data)
  local f, err = io.open(path, "wb")
  if not f then return nil, err end
  f:write(data)
  f:close()
  return true
end

local function write_atomic(path, data)
  local tmp = path .. ".tmp"
  local ok, err = write_file(tmp, data)
  if not ok then return nil, err end
  local renamed = os.rename(tmp, path)
  if not renamed then
    os.remove(tmp)
    return write_file(path, data)
  end
  return true
end

local function shell_quote(s)
  return "'" .. s:gsub("'", "'\\''") .. "'"
end

local function sh_ok(cmd)
  local a, b, c = os.execute(cmd)
  if a == true then return true end
  if type(a) == "number" then return a == 0 end
  return c == 0
end

local function run_capture(cmd)
  local p = io.popen(cmd, "r")
  if not p then return nil end
  local out = p:read("*a")
  p:close()
  return out
end

local function file_exists(path)
  return lfs.attributes(path) ~= nil
end

local function ensure_dir(path)
  if file_exists(path) then return true end
  local ok = lfs.mkdir(path)
  if ok then return true end
  return sh_ok("mkdir -p " .. shell_quote(path))
end

local function basename(path)
  return (path:match("([^/]+)$")) or path
end

local function dirname(path)
  local d = path:match("^(.*)/[^/]*$")
  return d or "."
end

local function normalize_path(path)
  local absolute = path:sub(1, 1) == "/"
  local parts = {}
  for part in path:gmatch("[^/]+") do
    if part == ".." then
      if #parts > 0 then parts[#parts] = nil end
    elseif part ~= "." then
      parts[#parts + 1] = part
    end
  end
  local out = table.concat(parts, "/")
  if absolute then out = "/" .. out end
  return out
end

local function path_without_extension(path)
  return (path:gsub("%.[^./]+$", ""))
end

local function json_encode(value)
  return json.encode(value)
end

local function json_decode(data)
  if not data or data == "" then return nil end
  local ok, value = pcall(json.decode, data)
  if not ok then return nil end
  return value
end

-- ---------------------------------------------------------------------------
-- Repository root and language configuration
-- ---------------------------------------------------------------------------

local function find_repo_root()
  local script_path = (arg and arg[0]) or "scripts/repo-intel.lua"
  local abs
  if script_path:sub(1, 1) == "/" then
    abs = normalize_path(script_path)
  else
    abs = normalize_path(lfs.currentdir() .. "/" .. script_path)
  end
  local cur = dirname(abs)
  for _ = 1, 10 do
    if lfs.attributes(cur .. "/.git") or lfs.attributes(cur .. "/RealKevbase.slnx") then
      return cur
    end
    local parent = dirname(cur)
    if parent == cur then break end
    cur = parent
  end
  return normalize_path(lfs.currentdir())
end

local ROOT = find_repo_root()
local CACHE_DIR = ROOT .. "/.repo-intel"
local INDEX_PATH = CACHE_DIR .. "/index.json"
local HASH_PATH = CACHE_DIR .. "/file-hashes.json"
local TS_CONFIG_PATH = CACHE_DIR .. "/tree-sitter.json"
local GRAMMAR_DIR = CACHE_DIR .. "/grammars"

local LANGS = {
  cs = {
    label = "C#",
    extensions = { [".cs"] = true },
    scope = "source.cs",
    grammar = "tree-sitter-c-sharp",
    url = "https://github.com/tree-sitter/tree-sitter-c-sharp.git",
  },
  js = {
    label = "JavaScript/JSX",
    extensions = { [".js"] = true, [".jsx"] = true, [".mjs"] = true, [".cjs"] = true },
    scope = "source.js",
    grammar = "tree-sitter-javascript",
    url = "https://github.com/tree-sitter/tree-sitter-javascript.git",
  },
  py = {
    label = "Python",
    extensions = { [".py"] = true },
    scope = "source.python",
    grammar = "tree-sitter-python",
    url = "https://github.com/tree-sitter/tree-sitter-python.git",
  },
}

local LANG_ORDER = { "cs", "js", "py" }

local function language_for(path)
  local ext = path:match("(%.[^./]+)$")
  if not ext then return nil end
  for _, key in ipairs(LANG_ORDER) do
    if LANGS[key].extensions[ext] then return key end
  end
  return nil
end

local function relative_path(abs_path)
  local prefix = ROOT .. "/"
  if abs_path:sub(1, #prefix) == prefix then
    return abs_path:sub(#prefix + 1)
  end
  return abs_path
end

-- ---------------------------------------------------------------------------
-- Content hashing (pure Lua, 2 x 32-bit DJB2; stable across runs)
-- ---------------------------------------------------------------------------

local function content_hash(data)
  local h1, h2 = 5381, 0
  for i = 1, #data do
    local byte = data:byte(i)
    h1 = (h1 * 33 + byte) % 4294967296
    h2 = (h2 * 131 + byte + i) % 4294967291
  end
  return string.format("%08x%08x", h1, h2)
end

local function count_loc(data)
  local count = 0
  for _ in data:gmatch("\n") do count = count + 1 end
  if #data > 0 and data:sub(-1) ~= "\n" then count = count + 1 end
  return count
end

-- ---------------------------------------------------------------------------
-- File scanning
-- ---------------------------------------------------------------------------

local EXCLUDED_DIRS = {
  [".git"] = true, [".repo-intel"] = true, [".semantic-search"] = true,
  [".venv"] = true, ["venv"] = true, ["node_modules"] = true,
  ["dist"] = true, ["dist-ssr"] = true, ["bin"] = true, ["obj"] = true,
  ["zz_tp"] = true, [".vs"] = true, [".vscode"] = true, [".idea"] = true,
  ["coverage"] = true, ["__pycache__"] = true, [".pytest_cache"] = true,
}

local function scan_source_files()
  local files = {}

  local function walk(rel_dir)
    local abs_dir = rel_dir == "" and ROOT or (ROOT .. "/" .. rel_dir)
    local names = {}
    local ok_dir = pcall(function()
      for name in lfs.dir(abs_dir) do
        if name ~= "." and name ~= ".." then names[#names + 1] = name end
      end
    end)
    if not ok_dir then return end
    table.sort(names)
    for _, name in ipairs(names) do
      local rel = rel_dir == "" and name or (rel_dir .. "/" .. name)
      local attr = lfs.attributes(ROOT .. "/" .. rel)
      if attr then
        if attr.mode == "directory" then
          if not EXCLUDED_DIRS[name] and name:sub(1, 1) ~= "." then
            walk(rel)
          end
        elseif attr.mode == "file" and language_for(rel) then
          files[#files + 1] = rel
        end
      end
    end
  end

  walk("")
  table.sort(files)
  return files
end

-- ---------------------------------------------------------------------------
-- Minimal XML parser for tree-sitter's --xml CST output
-- ---------------------------------------------------------------------------

local function xml_unescape(s)
  s = s:gsub("&lt;", "<")
  s = s:gsub("&gt;", ">")
  s = s:gsub("&quot;", '"')
  s = s:gsub("&apos;", "'")
  s = s:gsub("&amp;", "&")
  return s
end

local function parse_attrs(inner)
  local attrs = {}
  for key, value in inner:gmatch('([%w_%-:]+)%s*=%s*"([^"]*)"') do
    attrs[key] = xml_unescape(value)
  end
  return attrs
end

local function parse_xml(xml)
  local root = { tag = "__root__", attrs = {}, children = {}, text = "" }
  local stack = { root }
  local pos = 1
  local n = #xml

  while pos <= n do
    local lt = xml:find("<", pos, true)
    if not lt then
      local tail = xml:sub(pos)
      if tail ~= "" then
        local node = stack[#stack]
        node.children[#node.children + 1] = { tag = "__text__", text = xml_unescape(tail), is_text = true }
      end
      break
    end

    if lt > pos then
      local txt = xml:sub(pos, lt - 1)
      if txt:match("%S") then
        local node = stack[#stack]
        node.children[#node.children + 1] = { tag = "__text__", text = xml_unescape(txt), is_text = true }
      end
    end

    if xml:sub(lt, lt + 3) == "<!--" then
      local e = xml:find("-->", lt + 4, true)
      if not e then break end
      pos = e + 3
    elseif xml:sub(lt, lt + 1) == "<?" then
      local e = xml:find("?>", lt + 2, true)
      if not e then break end
      pos = e + 2
    elseif xml:sub(lt, lt + 1) == "<!" then
      local e = xml:find(">", lt + 2, true)
      if not e then break end
      pos = e + 1
    elseif xml:sub(lt, lt + 1) == "</" then
      local e = xml:find(">", lt + 2, true)
      if not e then break end
      local tag = xml:sub(lt + 2, e - 1):match("^%s*([^%s]+)")
      for i = #stack, 2, -1 do
        if stack[i].tag == tag then
          for _ = #stack, i, -1 do table.remove(stack) end
          break
        end
      end
      pos = e + 1
    else
      local i = lt + 1
      local quote = nil
      local e
      while i <= n do
        local c = xml:sub(i, i)
        if quote then
          if c == quote then quote = nil end
        elseif c == '"' or c == "'" then
          quote = c
        elseif c == ">" then
          e = i
          break
        end
        i = i + 1
      end
      if not e then break end

      local inner = xml:sub(lt + 1, e - 1)
      local self_close = inner:sub(-1) == "/"
      if self_close then inner = inner:sub(1, -2) end
      local tag = inner:match("^%s*([^%s/]+)")
      local attrs = parse_attrs(inner)
      local parent = stack[#stack]
      local node = { tag = tag, attrs = attrs, children = {}, text = "", parent = parent }
      node.index = #parent.children + 1
      parent.children[#parent.children + 1] = node
      if not self_close then stack[#stack + 1] = node end
      pos = e + 1
    end
  end

  return root
end

local function subtree_text(node)
  if not node then return "" end
  if node.is_text then return node.text end
  local parts = {}
  for _, child in ipairs(node.children) do
    parts[#parts + 1] = subtree_text(child)
  end
  return table.concat(parts)
end

local function element_children(node)
  local out = {}
  for _, child in ipairs(node.children) do
    if not child.is_text then out[#out + 1] = child end
  end
  return out
end

local function field_child(node, field)
  for _, child in ipairs(node.children) do
    if not child.is_text and child.attrs.field == field then return child end
  end
  return nil
end

local function first_child_tag(node, tag)
  for _, child in ipairs(node.children) do
    if not child.is_text and child.tag == tag then return child end
  end
  return nil
end

local function first_element(node)
  for _, child in ipairs(node.children) do
    if not child.is_text then return child end
  end
  return nil
end

local function preceding_comments(node)
  local comments = {}
  local parent = node.parent
  if not parent then return comments end
  for i = node.index - 1, 1, -1 do
    local sibling = parent.children[i]
    if sibling.is_text then
      -- whitespace/indentation between elements
    elseif sibling.tag == "comment" then
      table.insert(comments, 1, sibling)
    else
      break
    end
  end
  return comments
end

local function row(node)
  return tonumber(node.attrs.srow) or 0
end

local function col(node)
  return tonumber(node.attrs.scol) or 0
end

local function end_row(node)
  return tonumber(node.attrs.erow) or row(node)
end

local function end_col(node)
  return tonumber(node.attrs.ecol) or col(node)
end

-- ---------------------------------------------------------------------------
-- Doc comment cleaning
-- ---------------------------------------------------------------------------

local function strip_xml_tags(s)
  s = s:gsub('<see%s+cref="([^"]+)"%s*/>', "%1")
  s = s:gsub('<see%s+langword="([^"]+)"%s*/>', "%1")
  s = s:gsub('<paramref%s+name="([^"]+)"%s*/>', "%1")
  s = s:gsub('<typeparamref%s+name="([^"]+)"%s*/>', "%1")
  s = s:gsub('<seealso%s+cref="([^"]+)"%s*/>', "%1")
  return (s:gsub("<[^>]->", " "))
end

local function clean_cs_doc(comments)
  if #comments == 0 then return nil, nil end
  local raw_lines = {}
  for _, comment in ipairs(comments) do
    local text = subtree_text(comment)
    for line in (text .. "\n"):gmatch("(.-)\n") do
      line = line:gsub("^%s*///?", "")
      raw_lines[#raw_lines + 1] = line
    end
  end
  local raw = table.concat(raw_lines, "\n")
  local summary = raw:match("<summary>(.-)</summary>")
  if not summary then
    summary = raw:match("([^<\n][^\n]*)") or raw
  end
  local full = norm_ws(strip_xml_tags(raw))
  local short = norm_ws(strip_xml_tags(summary))
  if full == "" then return nil, nil end
  return truncate(full, 500), truncate(short, 180)
end

local function clean_js_doc(comments)
  if #comments == 0 then return nil, nil end
  local collected = {}
  for _, comment in ipairs(comments) do
    local text = subtree_text(comment)
    text = text:gsub("^%s*/%*%*?", ""):gsub("%*/%s*$", "")
    for line in (text .. "\n"):gmatch("(.-)\n") do
      line = line:gsub("^%s*//[/!]?", ""):gsub("^%s*%*%s?", "")
      collected[#collected + 1] = line
    end
  end
  local summary_lines = {}
  local full_lines = {}
  local in_summary = true
  for _, line in ipairs(collected) do
    line = trim(line)
    if line ~= "" then
      full_lines[#full_lines + 1] = line
      if in_summary then
        if line:sub(1, 1) == "@" then
          in_summary = false
        else
          summary_lines[#summary_lines + 1] = line
        end
      end
    elseif #summary_lines > 0 then
      in_summary = false
    end
  end
  local full = norm_ws(table.concat(full_lines, " "))
  local short = norm_ws(table.concat(summary_lines, " "))
  if full == "" then return nil, nil end
  return truncate(full, 500), truncate(short, 180)
end

local function clean_py_doc(comments)
  if #comments == 0 then return nil, nil end
  local lines = {}
  for _, comment in ipairs(comments) do
    for line in (subtree_text(comment) .. "\n"):gmatch("(.-)\n") do
      line = line:gsub("^%s*#%s?", "")
      if trim(line) ~= "" then lines[#lines + 1] = trim(line) end
    end
  end
  local text = norm_ws(table.concat(lines, " "))
  if text == "" then return nil, nil end
  return truncate(text, 500), truncate(text, 180)
end

local function python_docstring(node)
  local body = field_child(node, "body")
  if not body then return nil, nil end
  local first = first_element(body)
  if not first then return nil, nil end
  local str = nil
  if first.tag == "string" then
    str = first
  elseif first.tag == "expression_statement" then
    local inner = first_element(first)
    if inner and inner.tag == "string" then str = inner end
  end
  if not str then return nil, nil end
  local text = subtree_text(str)
  text = text:gsub('^%s*[rRuUbBfF]*"""', ""):gsub('"""%s*$', "")
  text = text:gsub("^%s*[rRuUbBfF]*'''", ""):gsub("'''%s*$", "")
  text = text:gsub("^%s*[rRuUbBfF]*\"", ""):gsub('"%s*$', "")
  text = text:gsub("^%s*[rRuUbBfF]*'", ""):gsub("'%s*$", "")
  text = norm_ws(text)
  if text == "" then return nil, nil end
  return truncate(text, 500), truncate(text, 180)
end

local function merge_docs(comments_doc, comments_summary, body_doc, body_summary)
  if comments_doc then return comments_doc, comments_summary end
  return body_doc, body_summary
end

-- ---------------------------------------------------------------------------
-- Signature extraction
-- ---------------------------------------------------------------------------

local CS_BODY_TAGS = {
  ["block"] = true,
  ["arrow_expression_clause"] = true,
  ["accessor_list"] = true,
  ["declaration_list"] = true,
  ["enum_member_declaration_list"] = true,
}

local JS_BODY_TAGS = {
  ["statement_block"] = true,
  ["class_body"] = true,
}

local function cs_header_end(node)
  for _, child in ipairs(node.children) do
    if not child.is_text and CS_BODY_TAGS[child.tag] then
      return row(child), col(child)
    end
  end
  return nil, nil
end

local function js_header_end(node)
  for _, child in ipairs(node.children) do
    if not child.is_text and JS_BODY_TAGS[child.tag] then
      return row(child), col(child)
    end
  end

  if node.tag == "variable_declarator" then
    local value = field_child(node, "value")
    if value and (value.tag == "arrow_function" or value.tag == "function_expression") then
      local params = field_child(value, "parameters") or field_child(value, "parameter")
      if params then return end_row(params), end_col(params) end
      local first = first_element(value)
      if first and first.tag == "identifier" then return end_row(first), end_col(first) end
    end
  end
  return nil, nil
end

local function strip_leading_attributes(sig)
  while sig:match("^%s*%[") do
    sig = sig:gsub("^%s*%[.-%]%s*", "", 1)
  end
  return sig
end

local function make_signature(lines, node, lang, kind)
  local sr, sc = row(node), col(node)
  local er, ec = end_row(node), end_col(node)

  if lang == "py" then
    local line = get_line(lines, sr):sub(sc + 1)
    line = line:gsub(":%s*$", "")
    return truncate(norm_ws(line), 200)
  end

  local br, bc
  if lang == "cs" then
    br, bc = cs_header_end(node)
  elseif lang == "js" then
    br, bc = js_header_end(node)
  end
  if br then er, ec = br, bc end

  local text = slice_range(lines, sr, sc, er, ec)
  text = strip_leading_attributes(text)
  text = norm_ws(text)
  text = text:gsub("%s*{%s*$", ""):gsub("%s*=>%s*$", ""):gsub("%s*;%s*$", "")

  if kind == "field" or kind == "const" or kind == "variable" then
    local before_eq = text:match("^(.-)%s*=")
    if before_eq and before_eq ~= "" then text = before_eq end
  end

  return truncate(norm_ws(text), 200)
end

-- ---------------------------------------------------------------------------
-- Reference collection (identifiers used inside a symbol)
-- ---------------------------------------------------------------------------

local REF_TAGS = {
  ["identifier"] = true,
  ["type_identifier"] = true,
  ["field_identifier"] = true,
  ["property_identifier"] = true,
  ["shorthand_property_identifier"] = true,
  ["jsx_identifier"] = true,
  ["jsx_namespace_name"] = true,
}

local CS_DECL_SKIP = {
  ["class_declaration"] = true, ["interface_declaration"] = true,
  ["struct_declaration"] = true, ["record_declaration"] = true,
  ["record_struct_declaration"] = true, ["enum_declaration"] = true,
  ["delegate_declaration"] = true, ["method_declaration"] = true,
  ["constructor_declaration"] = true, ["destructor_declaration"] = true,
  ["property_declaration"] = true, ["field_declaration"] = true,
  ["event_declaration"] = true, ["event_field_declaration"] = true,
  ["indexer_declaration"] = true, ["operator_declaration"] = true,
  ["local_function_statement"] = true,
}

local JS_DECL_SKIP = {
  ["function_declaration"] = true, ["generator_function_declaration"] = true,
  ["class_declaration"] = true, ["method_definition"] = true,
  ["lexical_declaration"] = true, ["variable_declaration"] = true,
  ["field_definition"] = true, ["public_field_definition"] = true,
}

local PY_DECL_SKIP = {
  ["function_definition"] = true, ["class_definition"] = true,
}

local function collect_refs(node, self_name, out, skip_set)
  local distinct = 0
  local function walk(current, is_root)
    for _, child in ipairs(current.children) do
      if not child.is_text then
        if (not is_root) and skip_set[child.tag] then
          -- Nested declaration: it has its own symbol and its own refs.
        elseif REF_TAGS[child.tag] then
          local text = norm_ws(subtree_text(child))
          if text ~= "" and text ~= self_name then
            if out[text] == nil then
              if distinct < 400 then
                out[text] = 1
                distinct = distinct + 1
              end
            else
              out[text] = out[text] + 1
            end
          end
          walk(child, false)
        else
          walk(child, false)
        end
      end
    end
  end
  walk(node, true)
end

-- ---------------------------------------------------------------------------
-- Import helpers
-- ---------------------------------------------------------------------------

local function parse_csharp_using(text)
  text = norm_ws(text):gsub(";%s*$", "")
  text = text:gsub("^global%s+", ""):gsub("^using%s+", ""):gsub("^static%s+", "")
  local alias, target = text:match("^([%w_]+)%s*=%s*(.+)$")
  if target then return compact_name(target), alias end
  return compact_name(text), nil
end

local function strip_quotes(s)
  s = trim(s)
  local inner = s:match('^"(.*)"$') or s:match("^'(.*)'$")
  return inner or s
end

local function js_import_names(node)
  local names = {}
  local clause = first_child_tag(node, "import_clause")
  if clause then
    for _, child in ipairs(clause.children) do
      if not child.is_text then
        if child.tag == "identifier" then
          names[#names + 1] = { imported = "default", local_name = subtree_text(child) }
        elseif child.tag == "named_imports" then
          for _, spec in ipairs(child.children) do
            if not spec.is_text and spec.tag == "import_specifier" then
              local name_node = field_child(spec, "name")
              local alias_node = field_child(spec, "alias")
              local imported = name_node and subtree_text(name_node) or nil
              local local_name = alias_node and subtree_text(alias_node) or imported
              if imported then names[#names + 1] = { imported = imported, local_name = local_name } end
            end
          end
        elseif child.tag == "namespace_import" then
          local id = nil
          for _, c in ipairs(child.children) do
            if not c.is_text and c.tag == "identifier" then id = subtree_text(c) end
          end
          if id then names[#names + 1] = { imported = "*", local_name = id } end
        end
      end
    end
  end
  return names
end

local function parse_python_import(text)
  text = norm_ws(text)
  local imports = {}
  local from, rest = text:match("^from%s+([%w_%.]+)%s+import%s+(.+)$")
  if from then
    local names = {}
    for part in rest:gmatch("[^,]+") do
      local name, alias = part:match("^%s*([%w_]+)%s+as%s+([%w_]+)%s*$")
      if not name then name = part:match("^%s*([%w_]+)%s*$") end
      if name and name ~= "*" then
        names[#names + 1] = { imported = name, local_name = alias or name }
      end
    end
    imports[#imports + 1] = { source = compact_name(from), names = names }
    return imports
  end
  local body = text:gsub("^import%s+", "")
  for part in body:gmatch("[^,]+") do
    local name, alias = part:match("^%s*([%w_%.]+)%s+as%s+([%w_]+)%s*$")
    if not name then name = part:match("^%s*([%w_%.]+)%s*$") end
    if name then
      imports[#imports + 1] = {
        source = compact_name(name),
        names = { { imported = name:match("([%w_]+)$") or name, local_name = alias or (name:match("([%w_]+)$") or name) } },
      }
    end
  end
  return imports
end

-- ---------------------------------------------------------------------------
-- Language extractors
-- ---------------------------------------------------------------------------

local CS_TYPE_TAGS = {
  class_declaration = "class",
  interface_declaration = "interface",
  struct_declaration = "struct",
  record_declaration = "record",
  record_struct_declaration = "record struct",
  enum_declaration = "enum",
  delegate_declaration = "delegate",
}

local CS_MEMBER_TAGS = {
  method_declaration = "method",
  constructor_declaration = "constructor",
  destructor_declaration = "destructor",
  property_declaration = "property",
  indexer_declaration = "indexer",
  field_declaration = "field",
  event_declaration = "event",
  event_field_declaration = "event",
  operator_declaration = "operator",
}

local function direct_modifiers(node)
  local mods = {}
  for _, child in ipairs(node.children) do
    if not child.is_text and child.tag == "modifier" then
      mods[#mods + 1] = norm_ws(subtree_text(child))
    end
  end
  return mods
end

local function first_direct_identifier(node)
  for _, child in ipairs(node.children) do
    if not child.is_text and child.tag == "identifier" then return child end
  end
  return nil
end

local function csharp_member_name(node)
  local name_node = field_child(node, "name")
  if name_node then return norm_ws(subtree_text(name_node)) end
  local declaration = first_child_tag(node, "variable_declaration")
  if declaration then
    for _, child in ipairs(declaration.children) do
      if not child.is_text and child.tag == "variable_declarator" then
        local declared = field_child(child, "name")
        if declared then return norm_ws(subtree_text(declared)) end
      end
    end
  end
  local identifier = first_direct_identifier(node)
  return identifier and norm_ws(subtree_text(identifier)) or ""
end

local function csharp_base_types(node)
  local base = field_child(node, "bases") or first_child_tag(node, "base_list")
  local out = {}
  if not base then return out end
  for _, child in ipairs(base.children) do
    if not child.is_text then
      local text = compact_name(subtree_text(child))
      if text ~= "" and text ~= "," then out[#out + 1] = text end
    end
  end
  return out
end

local function build_qualified(namespace, container, name)
  if container and container ~= "" then return container .. "." .. name end
  if namespace and namespace ~= "" then return namespace .. "." .. name end
  return name
end

local function new_symbol(fields)
  fields.refs = fields.refs or {}
  fields.base_types = fields.base_types or {}
  fields.kind = fields.kind or "symbol"
  fields.line_start = (fields.srow or 0) + 1
  fields.line_end = (fields.erow or fields.srow or 0) + 1
  return fields
end

local function extract_csharp(tree, source)
  local lines = split_lines(source)
  local symbols = {}
  local imports = {}
  local file_namespace = ""

  local function visit(node, ctx)
    for _, child in ipairs(node.children) do
      if not child.is_text then
        local tag = child.tag
        if tag == "using_directive" then
          local namespace, alias = parse_csharp_using(subtree_text(child))
          if namespace ~= "" then
            local names = {}
            if alias then
              names[1] = { imported = namespace:match("([%w_]+)$") or namespace, local_name = alias }
            end
            imports[#imports + 1] = { source = namespace, names = names }
          end
        elseif tag == "file_scoped_namespace_declaration" then
          -- In C# a file-scoped namespace declares the namespace for the rest of
          -- the compilation unit; the declarations are siblings, not children.
          local name_node = field_child(child, "name")
          local namespace = name_node and compact_name(subtree_text(name_node)) or ctx.namespace
          if file_namespace == "" then file_namespace = namespace end
          ctx.namespace = namespace
        elseif tag == "namespace_declaration" then
          local name_node = field_child(child, "name")
          local namespace = name_node and compact_name(subtree_text(name_node)) or ctx.namespace
          if file_namespace == "" then file_namespace = namespace end
          visit(child, { namespace = namespace, container = ctx.container, doc_anchor = nil, exported = ctx.exported })
        elseif CS_TYPE_TAGS[tag] then
          local name_node = field_child(child, "name") or first_direct_identifier(child)
          local name = name_node and norm_ws(subtree_text(name_node)) or ""
          if name ~= "" then
            local qualified = build_qualified(ctx.namespace, ctx.container, name)
            local doc, summary = clean_cs_doc(preceding_comments(child))
            local refs = {}
            collect_refs(child, name, refs, CS_DECL_SKIP)
            local symbol = new_symbol({
              name = name,
              qualified = qualified,
              kind = CS_TYPE_TAGS[tag],
              is_type = true,
              signature = make_signature(lines, child, "cs", CS_TYPE_TAGS[tag]),
              doc = doc,
              summary = summary,
              srow = row(child),
              erow = end_row(child),
              parent = ctx.container,
              base_types = csharp_base_types(child),
              refs = refs,
              exported = true,
            })
            symbols[#symbols + 1] = symbol
            visit(child, {
              namespace = ctx.namespace,
              container = qualified,
              doc_anchor = child,
              exported = ctx.exported,
            })
          end
        elseif CS_MEMBER_TAGS[tag] and ctx.container then
          local name = csharp_member_name(child)
          if name ~= "" then
            local kind = CS_MEMBER_TAGS[tag]
            local mods = direct_modifiers(child)
            if kind == "field" then
              for _, mod in ipairs(mods) do
                if mod == "const" then kind = "const" end
              end
            end
            local qualified = build_qualified(ctx.namespace, ctx.container, name)
            local doc, summary = clean_cs_doc(preceding_comments(child))
            local refs = {}
            collect_refs(child, name, refs, CS_DECL_SKIP)
            symbols[#symbols + 1] = new_symbol({
              name = name,
              qualified = qualified,
              kind = kind,
              is_type = false,
              signature = make_signature(lines, child, "cs", kind),
              doc = doc,
              summary = summary,
              srow = row(child),
              erow = end_row(child),
              parent = ctx.container,
              base_types = {},
              refs = refs,
              exported = true,
            })
          end
        else
          visit(child, ctx)
        end
      end
    end
  end

  visit(tree, { namespace = "", container = nil })
  table.sort(symbols, function(a, b)
    if a.srow ~= b.srow then return a.srow < b.srow end
    return a.name < b.name
  end)

  return {
    symbols = symbols,
    imports = imports,
    namespace = file_namespace,
    parse_errors = 0,
  }
end

local function js_value_shorthand(value)
  if not value then return nil end
  if value.tag == "object" then return "{...}" end
  if value.tag == "array" then return "[...]" end
  if value.tag == "string" then return strip_quotes(subtree_text(value)) end
  if value.tag == "number" then return subtree_text(value) end
  if value.tag == "true" or value.tag == "false" or value.tag == "null" then return subtree_text(value) end
  if value.tag == "call_expression" then
    local fn = field_child(value, "function")
    if fn then return norm_ws(subtree_text(fn)) .. "(...)" end
  end
  if value.tag == "new_expression" then
    local ctor = field_child(value, "constructor")
    if ctor then return "new " .. norm_ws(subtree_text(ctor)) .. "(...)" end
  end
  return nil
end

local function extract_javascript(tree, source)
  local lines = split_lines(source)
  local symbols = {}
  local imports = {}

  local function add_symbol(child, ctx, fields)
    fields.srow = row(child)
    fields.erow = end_row(child)
    fields.parent = ctx.container
    fields.doc, fields.summary = clean_js_doc(preceding_comments(ctx.doc_anchor or child))
    local refs = {}
    collect_refs(child, fields.name, refs, JS_DECL_SKIP)
    fields.refs = refs
    fields.exported = ctx.exported or false
    symbols[#symbols + 1] = new_symbol(fields)
  end

  local function visit(node, ctx)
    for _, child in ipairs(node.children) do
      if not child.is_text then
        local tag = child.tag
        if tag == "import_statement" then
          local source_node = field_child(child, "source")
          if source_node then
            imports[#imports + 1] = {
              source = clean_module_source(subtree_text(source_node)),
              names = js_import_names(child),
            }
          end
        elseif tag == "export_statement" then
          visit(child, {
            container = ctx.container,
            doc_anchor = child,
            exported = true,
            in_class = ctx.in_class,
          })
        elseif tag == "function_declaration" or tag == "generator_function_declaration" then
          local name_node = field_child(child, "name")
          local name = name_node and norm_ws(subtree_text(name_node)) or ""
          if name ~= "" then
            local qualified = build_qualified("", ctx.container, name)
            add_symbol(child, ctx, {
              name = name,
              qualified = qualified,
              kind = ctx.in_class and "method" or "function",
              is_type = false,
              signature = make_signature(lines, child, "js", "function"),
              base_types = {},
            })
          end
        elseif tag == "class_declaration" then
          local name_node = field_child(child, "name")
          local name = name_node and norm_ws(subtree_text(name_node)) or ""
          if name ~= "" then
            local qualified = build_qualified("", ctx.container, name)
            local bases = {}
            local heritage = first_child_tag(child, "class_heritage")
            if heritage then
              local text = compact_name(subtree_text(heritage))
              text = text:gsub("^extends%s+", "")
              if text ~= "" then bases[#bases + 1] = text end
            end
            add_symbol(child, ctx, {
              name = name,
              qualified = qualified,
              kind = "class",
              is_type = true,
              signature = make_signature(lines, child, "js", "class"),
              base_types = bases,
            })
            visit(child, {
              container = qualified,
              doc_anchor = child,
              exported = ctx.exported,
              in_class = true,
            })
          end
        elseif tag == "method_definition" or tag == "field_definition" or tag == "public_field_definition" then
          local name_node = field_child(child, "name")
          local name = name_node and norm_ws(subtree_text(name_node)) or ""
          if name ~= "" and ctx.in_class then
            local kind = "method"
            if name == "constructor" then kind = "constructor" end
            local value = field_child(child, "value")
            if tag ~= "method_definition" then
              kind = (value and (value.tag == "arrow_function" or value.tag == "function_expression")) and "method" or "field"
            end
            local qualified = build_qualified("", ctx.container, name)
            add_symbol(child, ctx, {
              name = name,
              qualified = qualified,
              kind = kind,
              is_type = false,
              signature = make_signature(lines, child, "js", kind),
              base_types = {},
            })
          end
        elseif tag == "lexical_declaration" or tag == "variable_declaration" then
          if not ctx.in_class then
            local keyword = norm_ws(subtree_text(child)):match("^(%a+)") or "const"
            for _, decl in ipairs(child.children) do
              if not decl.is_text and decl.tag == "variable_declarator" then
                local name_node = field_child(decl, "name")
                local name = name_node and norm_ws(subtree_text(name_node)) or ""
                if name ~= "" and name:match("^[%a_][%w_]*$") then
                  local value = field_child(decl, "value")
                  local is_function = value and (value.tag == "arrow_function" or value.tag == "function_expression")
                  local kind = is_function and "function" or (keyword == "const" and "const" or "variable")
                  local sig
                  if is_function then
                    sig = make_signature(lines, decl, "js", "function")
                    sig = sig:gsub("%s*{%s*$", "")
                    sig = truncate(norm_ws(sig) .. " =>", 200)
                  else
                    local short = js_value_shorthand(value)
                    sig = keyword .. " " .. name
                    if short then
                      sig = sig .. " = " .. short
                    end
                    sig = truncate(norm_ws(sig), 160)
                  end
                  local qualified = build_qualified("", ctx.container, name)
                  add_symbol(decl, ctx, {
                    name = name,
                    qualified = qualified,
                    kind = kind,
                    is_type = false,
                    signature = sig,
                    base_types = {},
                  })
                end
              end
            end
          end
        else
          visit(child, ctx)
        end
      end
    end
  end

  visit(tree, { container = nil, in_class = false, exported = false })
  table.sort(symbols, function(a, b)
    if a.srow ~= b.srow then return a.srow < b.srow end
    return a.name < b.name
  end)

  return {
    symbols = symbols,
    imports = imports,
    namespace = "",
    parse_errors = 0,
  }
end

local function extract_python(tree, source)
  local lines = split_lines(source)
  local symbols = {}
  local imports = {}

  local function add_symbol(child, ctx, fields)
    fields.srow = row(child)
    fields.erow = end_row(child)
    fields.parent = ctx.container
    local doc, summary = clean_py_doc(preceding_comments(ctx.doc_anchor or child))
    local body_doc, body_summary = python_docstring(child)
    fields.doc, fields.summary = merge_docs(doc, summary, body_doc, body_summary)
    local refs = {}
    collect_refs(child, fields.name, refs, PY_DECL_SKIP)
    fields.refs = refs
    fields.exported = true
    symbols[#symbols + 1] = new_symbol(fields)
  end

  local function visit(node, ctx)
    for _, child in ipairs(node.children) do
      if not child.is_text then
        local tag = child.tag
        if tag == "import_statement" or tag == "import_from_statement" then
          local parsed = parse_python_import(subtree_text(child))
          for _, entry in ipairs(parsed) do imports[#imports + 1] = entry end
        elseif tag == "decorated_definition" then
          visit(child, { container = ctx.container, doc_anchor = child })
        elseif tag == "class_definition" then
          local name_node = field_child(child, "name")
          local name = name_node and norm_ws(subtree_text(name_node)) or ""
          if name ~= "" then
            local qualified = build_qualified("", ctx.container, name)
            add_symbol(child, ctx, {
              name = name,
              qualified = qualified,
              kind = "class",
              is_type = true,
              signature = make_signature(lines, child, "py", "class"),
              base_types = {},
            })
            visit(child, { container = qualified, doc_anchor = child })
          end
        elseif tag == "function_definition" then
          local name_node = field_child(child, "name")
          local name = name_node and norm_ws(subtree_text(name_node)) or ""
          if name ~= "" then
            local qualified = build_qualified("", ctx.container, name)
            add_symbol(child, ctx, {
              name = name,
              qualified = qualified,
              kind = (ctx.container and (name == "__init__" and "constructor" or "method")) or "function",
              is_type = false,
              signature = make_signature(lines, child, "py", "function"),
              base_types = {},
            })
          end
        else
          visit(child, ctx)
        end
      end
    end
  end

  visit(tree, { container = nil })
  table.sort(symbols, function(a, b)
    if a.srow ~= b.srow then return a.srow < b.srow end
    return a.name < b.name
  end)

  return {
    symbols = symbols,
    imports = imports,
    namespace = "",
    parse_errors = 0,
  }
end

-- ---------------------------------------------------------------------------
-- tree-sitter invocation
-- ---------------------------------------------------------------------------

local function ensure_ts_config()
  ensure_dir(CACHE_DIR)
  local config = {
    ["parser-directories"] = { GRAMMAR_DIR },
  }
  return write_atomic(TS_CONFIG_PATH, json_encode(config))
end

local function grammar_available(lang)
  local spec = LANGS[lang]
  if not spec then return false end
  local dir = GRAMMAR_DIR .. "/" .. spec.grammar
  return file_exists(dir .. "/tree-sitter.json") or file_exists(dir .. "/src/parser.c")
end

local function run_tree_sitter(path, lang, rebuild)
  local spec = LANGS[lang]
  local cmd = "cd " .. shell_quote(ROOT) ..
    " && timeout 60 tree-sitter parse --config-path " .. shell_quote(TS_CONFIG_PATH) ..
    " -x --scope " .. shell_quote(spec.scope) ..
    (rebuild and " -r" or "") ..
    " " .. shell_quote(path) .. " 2>/dev/null"
  return run_capture(cmd)
end

local function parse_source_file(path, source, lang)
  if not grammar_available(lang) then
    return nil, "grammar not installed for " .. lang .. " (run: lua scripts/repo-intel.lua setup)"
  end

  local xml = run_tree_sitter(path, lang, false)
  if not xml or not xml:find("<?xml", 1, true) then
    xml = run_tree_sitter(path, lang, true)
  end
  if not xml or not xml:find("<?xml", 1, true) then
    return nil, "tree-sitter produced no CST"
  end

  local xml_root = parse_xml(xml)
  local tree = first_element(xml_root)
  if not tree then return nil, "empty CST" end

  local result
  if lang == "cs" then
    result = extract_csharp(tree, source)
  elseif lang == "js" then
    result = extract_javascript(tree, source)
  elseif lang == "py" then
    result = extract_python(tree, source)
  else
    return nil, "unsupported language"
  end

  local errors = 0
  for _ in xml:gmatch("<ERROR") do errors = errors + 1 end
  result.parse_errors = errors
  result.language = lang
  result.loc = count_loc(source)
  return result
end

-- ---------------------------------------------------------------------------
-- Index update (incremental, hash based)
-- ---------------------------------------------------------------------------

local function load_json_file(path)
  local data = read_file(path)
  return json_decode(data)
end

local function refresh_index(opts)
  opts = opts or {}
  local started = os.clock()
  ensure_ts_config()

  local old_index = load_json_file(INDEX_PATH)
  if not old_index or old_index.version ~= CACHE_VERSION then
    old_index = { version = CACHE_VERSION, files = {} }
  end
  local old_hashes = load_json_file(HASH_PATH) or { version = 1, files = {} }
  if not old_hashes.files then old_hashes.files = {} end

  local files = scan_source_files()
  local new_files = {}
  local new_hashes = {}
  local parsed, cached, removed, failed = 0, 0, 0, 0

  for _, path in ipairs(files) do
    local source, read_err = read_file(ROOT .. "/" .. path)
    if not source then
      failed = failed + 1
      io.stderr:write("repo-intel: warning: " .. (read_err or ("cannot read " .. path)) .. "\n")
    else
      local hash = content_hash(source)
      local lang = language_for(path)
      local old_entry = old_index.files[path]
      local old_hash = old_hashes.files[path]

      if old_entry and old_hash == hash and old_entry.lang == lang and not opts.force then
        new_files[path] = old_entry
        cached = cached + 1
      else
        local parsed_entry, err = parse_source_file(path, source, lang)
        if parsed_entry then
          parsed_entry.hash = hash
          parsed_entry.lang = lang
          new_files[path] = parsed_entry
          parsed = parsed + 1
        else
          failed = failed + 1
          io.stderr:write("repo-intel: warning: " .. path .. ": " .. (err or "parse failed") .. "\n")
          new_files[path] = {
            hash = hash,
            lang = lang,
            loc = count_loc(source),
            symbols = {},
            imports = {},
            namespace = "",
            parse_errors = 1,
            error = err or "parse failed",
          }
        end
      end
      new_hashes[path] = hash
    end
  end

  for path in pairs(old_index.files) do
    if not new_files[path] then removed = removed + 1 end
  end

  local index = {
    version = CACHE_VERSION,
    generatedAt = os.date("!%Y-%m-%dT%H:%M:%SZ"),
    files = new_files,
  }

  local hash_doc = {
    version = 1,
    generatedAt = index.generatedAt,
    files = new_hashes,
  }

  ensure_dir(CACHE_DIR)
  local ok1, err1 = write_atomic(INDEX_PATH, json_encode(index))
  local ok2, err2 = write_atomic(HASH_PATH, json_encode(hash_doc))
  if not ok1 then io.stderr:write("repo-intel: cannot write index: " .. tostring(err1) .. "\n") end
  if not ok2 then io.stderr:write("repo-intel: cannot write hashes: " .. tostring(err2) .. "\n") end

  return index, {
    files = #files,
    parsed = parsed,
    cached = cached,
    removed = removed,
    failed = failed,
    elapsed = os.clock() - started,
  }
end

-- ---------------------------------------------------------------------------
-- Module resolution
-- ---------------------------------------------------------------------------

local function build_file_lookups(index)
  local by_namespace = {}
  local by_basename = {}
  local by_path = {}
  for path, file in pairs(index.files) do
    by_path[path] = file
    if file.namespace and file.namespace ~= "" then
      by_namespace[file.namespace] = by_namespace[file.namespace] or {}
      table.insert(by_namespace[file.namespace], path)
    end
    local base = basename(path):gsub("%.[^.]+$", "")
    by_basename[base] = by_basename[base] or {}
    table.insert(by_basename[base], path)
  end
  return by_namespace, by_basename, by_path
end

local function resolve_js_import(from_path, source, by_path)
  if source:sub(1, 1) ~= "." then return {} end
  local base = normalize_path(dirname(from_path) .. "/" .. source)
  local candidates = {
    base,
    base .. ".js", base .. ".jsx", base .. ".mjs", base .. ".cjs",
    base .. "/index.js", base .. "/index.jsx",
  }
  local out = {}
  for _, candidate in ipairs(candidates) do
    if by_path[candidate] then
      out[#out + 1] = candidate
      return out
    end
  end
  return out
end

local function resolve_python_import(from_path, source, by_path, by_basename)
  local out = {}
  if source:sub(1, 1) == "." then
    local dots = source:match("^(%.+)") or ""
    local rest = source:sub(#dots + 1):gsub("%.", "/")
    local base = dirname(from_path)
    for _ = 2, #dots do base = dirname(base) end
    local candidate = normalize_path(base .. "/" .. rest .. ".py")
    if by_path[candidate] then out[#out + 1] = candidate end
    local pkg = normalize_path(base .. "/" .. rest .. "/__init__.py")
    if by_path[pkg] then out[#out + 1] = pkg end
    return out
  end
  local direct = normalize_path(dirname(from_path) .. "/" .. source:gsub("%.", "/") .. ".py")
  if by_path[direct] then out[#out + 1] = direct end
  local name = source:match("([%w_]+)$")
  if name and by_basename[name] then
    for _, path in ipairs(by_basename[name]) do
      if path ~= from_path then out[#out + 1] = path end
    end
  end
  return out
end

local function resolve_import(from_path, lang, entry, lookups)
  local by_namespace, by_basename, by_path = lookups[1], lookups[2], lookups[3]
  local by_type_name = lookups[4]
  if lang == "cs" then
    local targets = by_namespace[entry.source]
    if targets then return targets end
    -- `using Alias = Namespace.Type;` refers to a type, not a namespace.
    local simple = entry.source:match("([%w_]+)$")
    if simple and by_type_name and by_type_name[simple] then
      return by_type_name[simple]
    end
    return {}
  elseif lang == "js" then
    return resolve_js_import(from_path, entry.source, by_path)
  elseif lang == "py" then
    return resolve_python_import(from_path, entry.source, by_path, by_basename)
  end
  return {}
end

-- ---------------------------------------------------------------------------
-- Graph construction and PageRank
-- ---------------------------------------------------------------------------

local KIND_WEIGHT = {
  interface = 2.2,
  class = 1.35,
  record = 1.25,
  ["record struct"] = 1.25,
  struct = 1.2,
  enum = 1.15,
  delegate = 1.1,
  ["function"] = 1.05,
  method = 1.0,
  constructor = 0.95,
  property = 0.6,
  event = 0.6,
  field = 0.45,
  const = 0.75,
  variable = 0.5,
  indexer = 0.8,
  operator = 0.8,
  symbol = 1.0,
}

local function is_key_symbol(symbol)
  if symbol.kind == "class" then
    for _, base in ipairs(symbol.base_types or {}) do
      if base == "Error" then return false end
    end
  end
  if symbol.is_type then return true end
  if symbol.kind == "method" or symbol.kind == "function" then return true end
  if symbol.kind == "const" and symbol.lang == "js" and symbol.exported then return true end
  return false
end

local function pagerank(ids, edges, damping, iterations)
  local n = #ids
  if n == 0 then return {} end
  local index_of = {}
  for i, id in ipairs(ids) do index_of[id] = i end

  local outgoing = {}
  local incoming = {}
  for i = 1, n do
    outgoing[i] = {}
    incoming[i] = {}
  end
  for _, edge in ipairs(edges) do
    local a, b = index_of[edge.from], index_of[edge.to]
    if a and b and a ~= b then
      outgoing[a][#outgoing[a] + 1] = { to = b, weight = edge.weight }
      incoming[b][#incoming[b] + 1] = { from = a, weight = edge.weight }
    end
  end

  local rank = {}
  for i = 1, n do rank[i] = 1 / n end

  for _ = 1, iterations do
    local dangling = 0
    for i = 1, n do
      if #outgoing[i] == 0 then dangling = dangling + rank[i] end
    end
    local base = (1 - damping) / n + damping * dangling / n
    local next_rank = {}
    for i = 1, n do next_rank[i] = base end
    for i = 1, n do
      local out = outgoing[i]
      if #out > 0 then
        local total = 0
        for _, edge in ipairs(out) do total = total + edge.weight end
        if total > 0 then
          local share = damping * rank[i] / total
          for _, edge in ipairs(out) do
            next_rank[edge.to] = next_rank[edge.to] + share * edge.weight
          end
        end
      end
    end
    local sum = 0
    for i = 1, n do sum = sum + next_rank[i] end
    if sum > 0 then
      for i = 1, n do next_rank[i] = next_rank[i] / sum end
    end
    rank = next_rank
  end

  local out = {}
  for id, i in pairs(index_of) do out[id] = rank[i] end
  return out
end

local function derive(index)
  local paths = {}
  for path in pairs(index.files) do paths[#paths + 1] = path end
  table.sort(paths)

  local by_namespace, by_basename, by_path = build_file_lookups(index)
  local by_type_name = {}
  for path, file in pairs(index.files) do
    for _, symbol in ipairs(file.symbols or {}) do
      if symbol.is_type then
        by_type_name[symbol.name] = by_type_name[symbol.name] or {}
        local list = by_type_name[symbol.name]
        local found = false
        for _, existing in ipairs(list) do if existing == path then found = true end end
        if not found then list[#list + 1] = path end
      end
    end
  end
  local lookups = { by_namespace, by_basename, by_path, by_type_name }

  -- Resolve imports and build alias maps (imported name -> local usage).
  local resolved_imports = {}
  local external_counts = {}
  for _, path in ipairs(paths) do
    local file = index.files[path]
    resolved_imports[path] = {}
    file.aliases = {}
    for i, entry in ipairs(file.imports or {}) do
      local targets = resolve_import(path, file.lang, entry, lookups)
      resolved_imports[path][i] = targets
      if #targets == 0 and entry.source ~= "" then
        if not external_counts[entry.source] then
          external_counts[entry.source] = { count = 0, lang = file.lang }
        end
        external_counts[entry.source].count = external_counts[entry.source].count + 1
      end
      if #targets > 0 then
        for _, name in ipairs(entry.names or {}) do
          if name.local_name and name.imported and name.imported ~= "*" and name.imported ~= "default" then
            file.aliases[name.local_name] = name.imported
          end
        end
      end
    end
  end

  -- Collect symbols with deterministic unique ids.
  local symbols = {}
  local by_name = {}
  local by_qualified = {}
  local by_file_qualified = {}
  local by_file_name = {}
  local by_scope_name = {}
  local scope_for = {}
  local seen = {}

  for _, path in ipairs(paths) do
    local file = index.files[path]
    table.sort(file.symbols or {}, function(a, b)
      if a.srow ~= b.srow then return a.srow < b.srow end
      return a.name < b.name
    end)
    local per_file = {}
    for _, symbol in ipairs(file.symbols or {}) do
      symbol.file = path
      symbol.lang = file.lang
      local base_id = path .. "::" .. symbol.qualified
      local uid = base_id
      local suffix = 2
      while seen[uid] do
        uid = base_id .. "#" .. suffix
        suffix = suffix + 1
      end
      seen[uid] = true
      symbol.uid = uid
      symbols[#symbols + 1] = symbol
      by_name[symbol.name] = by_name[symbol.name] or {}
      table.insert(by_name[symbol.name], symbol)
      by_file_name[path] = by_file_name[path] or {}
      by_file_name[path][symbol.name] = by_file_name[path][symbol.name] or {}
      table.insert(by_file_name[path][symbol.name], symbol)
      local scope = (file.namespace and file.namespace ~= "") and file.namespace or dirname(path)
      scope_for[path] = scope
      by_scope_name[scope] = by_scope_name[scope] or {}
      by_scope_name[scope][symbol.name] = by_scope_name[scope][symbol.name] or {}
      table.insert(by_scope_name[scope][symbol.name], symbol)
      by_qualified[symbol.qualified] = by_qualified[symbol.qualified] or {}
      table.insert(by_qualified[symbol.qualified], symbol)
      per_file[symbol.qualified] = per_file[symbol.qualified] or symbol
    end
    by_file_qualified[path] = per_file
  end

  -- Parent links and symbols-by-name index.
  for _, symbol in ipairs(symbols) do
    if symbol.parent then
      local parent = by_file_qualified[symbol.file] and by_file_qualified[symbol.file][symbol.parent]
      if parent and parent.uid ~= symbol.uid then symbol.parent_uid = parent.uid end
    end
  end

  -- Prefer same-file, then same-namespace/directory targets. This avoids
  -- common-name collisions (for example the many `_logger` fields).
  local function candidates_for(name, symbol)
    local same_file = by_file_name[symbol.file] and by_file_name[symbol.file][name]
    if same_file and #same_file > 0 then return same_file end
    local scope = scope_for[symbol.file]
    local same_scope = scope and by_scope_name[scope] and by_scope_name[scope][name]
    if same_scope and #same_scope > 0 then return same_scope end
    return by_name[name] or {}
  end

  -- Symbol graph.
  local edge_map = {}
  local function add_edge(from, to, weight, kind)
    if not from or not to or from == to then return end
    local key = from .. "\0" .. to
    local edge = edge_map[key]
    if edge then
      edge.weight = edge.weight + weight
      if kind == "base" then edge.kind = "base" end
    else
      edge_map[key] = { from = from, to = to, weight = weight, kind = kind or "ref" }
    end
  end

  for _, symbol in ipairs(symbols) do
    if symbol.parent_uid then
      add_edge(symbol.parent_uid, symbol.uid, 0.35, "contains")
      -- Behavioural members lend importance back to their type; data fields
      -- do not, otherwise option/config records dominate the ranking.
      if symbol.kind == "method" or symbol.kind == "function" then
        add_edge(symbol.uid, symbol.parent_uid, 0.5, "member-of")
      end
    end
    for _, base in ipairs(symbol.base_types or {}) do
      local simple = base:match("([%w_%.]+)$") or base
      simple = simple:match("([%w_]+)$") or simple
      local targets = candidates_for(simple, symbol)
      for _, target in ipairs(targets) do
        add_edge(symbol.uid, target.uid, 3.0 / math.max(#targets, 1), "base")
      end
    end
    local aliases = (index.files[symbol.file] and index.files[symbol.file].aliases) or {}
    for ref_name, count in pairs(symbol.refs or {}) do
      local mapped = aliases[ref_name] or ref_name
      local targets = candidates_for(mapped, symbol)
      if #targets == 0 and mapped ~= ref_name then
        targets = candidates_for(ref_name, symbol)
      end
      if #targets > 0 then
        local weight = (1 + math.log(1 + count)) / #targets
        for _, target in ipairs(targets) do
          add_edge(symbol.uid, target.uid, weight, "ref")
          if symbol.parent_uid then
            local share = 0
            if symbol.kind == "property" or symbol.kind == "field" or
                symbol.kind == "const" or symbol.kind == "variable" or symbol.kind == "event" then
              share = 0.5
            elseif symbol.kind == "method" or symbol.kind == "function" then
              share = 0.2
            end
            if share > 0 then
              add_edge(symbol.parent_uid, target.uid, weight * share, "ref")
            end
          end
        end
      end
    end
  end

  table.sort(symbols, function(a, b)
    if a.file ~= b.file then return a.file < b.file end
    if a.srow ~= b.srow then return a.srow < b.srow end
    return a.name < b.name
  end)
  local symbol_ids = {}
  for _, symbol in ipairs(symbols) do symbol_ids[#symbol_ids + 1] = symbol.uid end
  local symbol_edges = {}
  for _, edge in pairs(edge_map) do symbol_edges[#symbol_edges + 1] = edge end
  table.sort(symbol_edges, function(a, b)
    if a.from ~= b.from then return a.from < b.from end
    return a.to < b.to
  end)

  local symbol_rank = pagerank(symbol_ids, symbol_edges, DAMPING, PAGERANK_ITERATIONS)

  -- File graph from resolved imports.
  local file_edge_map = {}
  for _, path in ipairs(paths) do
    local file = index.files[path]
    for i, entry in ipairs(file.imports or {}) do
      local targets = resolved_imports[path][i] or {}
      for _, target in ipairs(targets) do
        if target ~= path then
          local key = path .. "\1" .. target
          local weight = 1 / math.max(#targets, 1)
          file_edge_map[key] = (file_edge_map[key] or 0) + weight
        end
      end
    end
  end
  local file_edges = {}
  for key, weight in pairs(file_edge_map) do
    local separator = key:find("\1", 1, true)
    if separator then
      local from = key:sub(1, separator - 1)
      local to = key:sub(separator + 1)
      file_edges[#file_edges + 1] = { from = from, to = to, weight = weight }
    end
  end
  table.sort(file_edges, function(a, b)
    if a.from ~= b.from then return a.from < b.from end
    return a.to < b.to
  end)
  local file_rank = pagerank(paths, file_edges, DAMPING, PAGERANK_ITERATIONS)
  local max_file_rank = 0
  for _, value in pairs(file_rank) do if value > max_file_rank then max_file_rank = value end end
  if max_file_rank <= 0 then max_file_rank = 1 end

  for _, symbol in ipairs(symbols) do
    local base_rank = symbol_rank[symbol.uid] or 0
    local file_norm = (file_rank[symbol.file] or 0) / max_file_rank
    local kind_weight = KIND_WEIGHT[symbol.kind] or 1
    -- Configuration containers are useful but not architectural centres.
    if symbol.kind == "class" and symbol.name:match("Options$") then
      kind_weight = kind_weight * 0.45
    end
    symbol.score = base_rank * (1 + 0.25 * file_norm) * kind_weight
    symbol.base_rank = base_rank
    symbol.file_rank = file_rank[symbol.file] or 0
  end

  -- Incoming/outgoing adjacency for symbol queries.
  local outgoing = {}
  local incoming = {}
  for _, edge in ipairs(symbol_edges) do
    outgoing[edge.from] = outgoing[edge.from] or {}
    incoming[edge.to] = incoming[edge.to] or {}
    if edge.kind ~= "contains" and edge.kind ~= "member-of" then
      outgoing[edge.from][#outgoing[edge.from] + 1] = edge
      incoming[edge.to][#incoming[edge.to] + 1] = edge
    end
  end

  local symbol_by_uid = {}
  for _, symbol in ipairs(symbols) do symbol_by_uid[symbol.uid] = symbol end

  local external = {}
  for name, info in pairs(external_counts) do
    external[#external + 1] = { name = name, count = info.count, lang = info.lang }
  end
  table.sort(external, function(a, b)
    if a.count ~= b.count then return a.count > b.count end
    return a.name < b.name
  end)

  return {
    paths = paths,
    symbols = symbols,
    symbol_by_uid = symbol_by_uid,
    by_name = by_name,
    by_qualified = by_qualified,
    symbol_edges = symbol_edges,
    symbol_rank = symbol_rank,
    file_rank = file_rank,
    file_edges = file_edges,
    outgoing = outgoing,
    incoming = incoming,
    resolved_imports = resolved_imports,
    lookups = lookups,
    external = external,
    symbol_ids = symbol_ids,
  }
end

-- ---------------------------------------------------------------------------
-- Token accounting
-- ---------------------------------------------------------------------------

-- Character-based token estimates, deliberately conservative for the two
-- output styles: prose/Markdown tokenizes denser than code/paths. These keep
-- the printed budgets under the requested ceiling even without a BPE encoder.
local function estimate_tokens(text, mode)
  if not text or text == "" then return 0 end
  local divisor = (mode == "prose") and 3.2 or 3.7
  return math.ceil(#text / divisor)
end

local function format_tokens(n)
  return string.format("~%d tokens", n)
end

-- ---------------------------------------------------------------------------
-- Resolvers for CLI queries
-- ---------------------------------------------------------------------------

local function symbol_matches(derived, query)
  local matches = {}
  local seen = {}
  local function add(list)
    for _, symbol in ipairs(list) do
      if not seen[symbol.uid] then
        seen[symbol.uid] = true
        matches[#matches + 1] = symbol
      end
    end
  end

  if derived.by_name[query] then add(derived.by_name[query]) end
  if derived.by_qualified[query] then add(derived.by_qualified[query]) end

  if #matches == 0 then
    local lower = query:lower()
    for name, list in pairs(derived.by_name) do
      if name:lower() == lower then add(list) end
    end
    for qualified, list in pairs(derived.by_qualified) do
      if qualified:lower():sub(-#lower) == lower then add(list) end
    end
  end

  table.sort(matches, function(a, b)
    if (a.is_type and true or false) ~= (b.is_type and true or false) then return a.is_type and true or false end
    if a.score ~= b.score then return a.score > b.score end
    return a.uid < b.uid
  end)
  return matches
end

local function module_matches(derived, index, query)
  local out = {}
  local seen = {}
  local function add(path)
    if path and not seen[path] then
      seen[path] = true
      out[#out + 1] = path
    end
  end

  if index.files[query] then add(query) end
  if #out == 0 and (query:find("/", 1, true) or query:match("%.[%a]+$")) then
    local suffix_hits = {}
    for _, path in ipairs(derived.paths) do
      if path:sub(-#query) == query then suffix_hits[#suffix_hits + 1] = path end
    end
    if #suffix_hits == 1 then add(suffix_hits[1]) end
  end
  if #out == 0 then
    local ns_files = derived.lookups[1][query]
    if ns_files then
      local sorted = {}
      for _, path in ipairs(ns_files) do sorted[#sorted + 1] = path end
      table.sort(sorted)
      for _, path in ipairs(sorted) do add(path) end
    end
  end
  if #out == 0 then
    for base, paths in pairs(derived.lookups[2]) do
      if base == query then
        table.sort(paths)
        for _, path in ipairs(paths) do add(path) end
      end
    end
  end
  return out
end

local function looks_like_module(query)
  return query:find("/", 1, true) ~= nil or query:find("%.") ~= nil
end

-- ---------------------------------------------------------------------------
-- Rendering: overview
-- ---------------------------------------------------------------------------

local function human_int(n)
  if n >= 1000 then
    return string.format("%.1fk", n / 1000)
  end
  return tostring(n)
end

local function repo_name()
  local ok, iterator, state = pcall(lfs.dir, ROOT)
  if ok then
    for name in iterator, state do
      if name:match("%.slnx?$") then return (name:gsub("%.slnx?$", "")) end
    end
  end
  return basename(ROOT)
end

local function project_for_path(path)
  local project = path:match("^src/([^/]+)/")
  if project then return project end
  return "scripts"
end

local function build_overview(index, derived, symbol_limit, include_docs)
  local lines = {}
  local function add(line) lines[#lines + 1] = line end

  -- Aggregate language and project statistics.
  local lang_stats = {}
  local project_stats = {}
  local total_loc, total_symbols = 0, 0
  for _, path in ipairs(derived.paths) do
    local file = index.files[path]
    local lang = file.lang or "?"
    lang_stats[lang] = lang_stats[lang] or { files = 0, loc = 0, symbols = 0 }
    lang_stats[lang].files = lang_stats[lang].files + 1
    lang_stats[lang].loc = lang_stats[lang].loc + (file.loc or 0)
    lang_stats[lang].symbols = lang_stats[lang].symbols + #(file.symbols or {})
    total_loc = total_loc + (file.loc or 0)
    total_symbols = total_symbols + #(file.symbols or {})

    local project = project_for_path(path)
    project_stats[project] = project_stats[project] or {
      files = 0, loc = 0, symbols = 0, rank = 0, entry = nil, kind = "module",
    }
    project_stats[project].files = project_stats[project].files + 1
    project_stats[project].loc = project_stats[project].loc + (file.loc or 0)
    project_stats[project].symbols = project_stats[project].symbols + #(file.symbols or {})
    project_stats[project].rank = project_stats[project].rank + (derived.file_rank[path] or 0)
    if file.lang == "cs" or file.lang == "js" then
      project_stats[project].kind = file.lang == "cs" and ".NET" or "React"
    end
    local base = basename(path)
    if base == "Program.cs" or base == "main.jsx" or base == "main.js" then
      project_stats[project].entry = path
    end
  end

  local module_stats = {}
  local top_symbols = {}
  for _, symbol in ipairs(derived.symbols) do
    local file = index.files[symbol.file]
    local key = (file.namespace and file.namespace ~= "") and file.namespace or dirname(symbol.file)
    module_stats[key] = module_stats[key] or { files = {}, rank = 0, symbols = {} }
    module_stats[key].files[symbol.file] = true
    if is_key_symbol(symbol) then
      module_stats[key].symbols[#module_stats[key].symbols + 1] = symbol
      top_symbols[#top_symbols + 1] = symbol
    end
  end
  local modules = {}
  for path, _ in pairs(index.files) do
    local file = index.files[path]
    local key = (file.namespace and file.namespace ~= "") and file.namespace or dirname(path)
    module_stats[key] = module_stats[key] or { files = {}, rank = 0, symbols = {} }
    module_stats[key].files[path] = true
    module_stats[key].rank = module_stats[key].rank + (derived.file_rank[path] or 0)
  end
  for key, stat in pairs(module_stats) do
    local count = 0
    for _ in pairs(stat.files) do count = count + 1 end
    stat.name = key
    stat.file_count = count
    stat.symbols = stat.symbols
    modules[#modules + 1] = stat
  end
  table.sort(modules, function(a, b)
    if a.rank ~= b.rank then return a.rank > b.rank end
    return a.name < b.name
  end)
  table.sort(top_symbols, function(a, b)
    if a.score ~= b.score then return a.score > b.score end
    return a.uid < b.uid
  end)
  while #top_symbols > symbol_limit do table.remove(top_symbols) end

  add("# repo-intel overview — " .. repo_name())
  add(string.format("%d files · %s LOC · %d symbols · %d edges · indexed %s",
    #derived.paths, human_int(total_loc), total_symbols, #derived.symbol_edges,
    (index.generatedAt or "?"):gsub("T", " "):gsub("Z", " UTC")))
  add("")
  add("## Languages")
  for _, lang in ipairs(LANG_ORDER) do
    local stat = lang_stats[lang]
    if stat then
      add(string.format("- %s: %d files, %s LOC, %d symbols",
        LANGS[lang].label, stat.files, human_int(stat.loc), stat.symbols))
    end
  end

  add("")
  add("## Projects")
  local projects = {}
  for name, stat in pairs(project_stats) do stat.name = name; projects[#projects + 1] = stat end
  table.sort(projects, function(a, b)
    if a.rank ~= b.rank then return a.rank > b.rank end
    return a.name < b.name
  end)
  for i = 1, math.min(#projects, 3) do
    local p = projects[i]
    local entry = p.entry and (" · entry " .. basename(p.entry)) or ""
    add(string.format("- %s (%s): %d files, %d symbols%s", p.name, p.kind, p.files, p.symbols, entry))
  end

  add("")
  add("## Core modules (import PageRank)")
  for i = 1, math.min(#modules, 4) do
    local module = modules[i]
    table.sort(module.symbols, function(a, b)
      if a.score ~= b.score then return a.score > b.score end
      return a.uid < b.uid
    end)
    local names = {}
    for j = 1, math.min(#module.symbols, 3) do names[#names + 1] = module.symbols[j].name end
    add(string.format("- `%s` (%d files) — %s", module.name, module.file_count,
      #names > 0 and table.concat(names, ", ") or "no key symbols"))
  end

  add("")
  add("## Key symbols (symbol PageRank)")
  for _, symbol in ipairs(top_symbols) do
    local location = string.format("%s:%d", symbol.file, symbol.line_start)
    local summary = include_docs and symbol.summary and symbol.summary ~= "" and
      (" — " .. truncate(symbol.summary, 92)) or ""
    add(string.format("- `%s` (%s) — %s%s", symbol.name, symbol.kind, location, summary))
  end

  local externals = {}
  for i = 1, math.min(#derived.external, 5) do
    local ext = derived.external[i]
    if ext.lang == "js" and ext.name:sub(1, 1) ~= "." then externals[#externals + 1] = ext.name end
  end
  if #externals > 0 then
    add("")
    add("## External packages")
    add("- " .. table.concat(externals, ", "))
  end

  add("")
  add("Refresh: `lua scripts/repo-intel.lua update` · cache `.repo-intel/`")
  return table.concat(lines, "\n") .. "\n"
end

-- ---------------------------------------------------------------------------
-- Rendering: map
-- ---------------------------------------------------------------------------

local function build_map(derived, budget)
  local header = string.format("# repo-intel map · %d symbols · budget %d tokens\n", #derived.symbols, budget)
  local used = estimate_tokens(header) + 30

  local candidates = {}
  for _, symbol in ipairs(derived.symbols) do
    if is_key_symbol(symbol) then candidates[#candidates + 1] = symbol end
  end
  table.sort(candidates, function(a, b)
    if a.score ~= b.score then return a.score > b.score end
    return a.uid < b.uid
  end)

  -- Select by rank, then group by file so a file header is never separated
  -- from its symbols (the map stays readable and Aider-like).
  local groups = {}
  local group_order = {}
  for _, symbol in ipairs(candidates) do
    local sig = truncate(symbol.signature or symbol.name, 170)
    local line = string.format("  %s (L%d-%d)\n", sig, symbol.line_start, symbol.line_end)
    local cost = estimate_tokens(line)
    local group = groups[symbol.file]
    if not group then
      cost = cost + estimate_tokens(symbol.file .. "\n")
    end
    if used + cost <= budget then
      if not group then
        group = { file = symbol.file, symbols = {}, best_score = symbol.score }
        groups[symbol.file] = group
        group_order[#group_order + 1] = group
      end
      group.symbols[#group.symbols + 1] = symbol
      if symbol.score > group.best_score then group.best_score = symbol.score end
      used = used + cost
    end
  end

  table.sort(group_order, function(a, b)
    if a.best_score ~= b.best_score then return a.best_score > b.best_score end
    return a.file < b.file
  end)

  local parts = { header }
  local shown = 0
  for _, group in ipairs(group_order) do
    parts[#parts + 1] = group.file .. "\n"
    table.sort(group.symbols, function(a, b)
      if a.score ~= b.score then return a.score > b.score end
      return a.srow < b.srow
    end)
    for _, symbol in ipairs(group.symbols) do
      local sig = truncate(symbol.signature or symbol.name, 170)
      parts[#parts + 1] = string.format("  %s (L%d-%d)\n", sig, symbol.line_start, symbol.line_end)
      shown = shown + 1
    end
  end
  local text = table.concat(parts, "")
  text = text .. string.format("\n%d symbols shown · %s of %d budget\n",
    shown, format_tokens(estimate_tokens(text)), budget)
  return text
end

-- ---------------------------------------------------------------------------
-- Rendering: symbol
-- ---------------------------------------------------------------------------

local function symbol_location(symbol)
  return string.format("%s:%d-%d", symbol.file, symbol.line_start, symbol.line_end)
end

local function related_edges(derived, uid, direction, limit)
  local edges
  if direction == "out" then
    edges = derived.outgoing[uid]
  else
    edges = derived.incoming[uid]
  end
  if not edges then return {} end
  local sorted = {}
  for _, edge in ipairs(edges) do sorted[#sorted + 1] = edge end
  table.sort(sorted, function(a, b)
    if a.weight ~= b.weight then return a.weight > b.weight end
    local ao = derived.symbol_by_uid[direction == "out" and a.to or a.from]
    local bo = derived.symbol_by_uid[direction == "out" and b.to or b.from]
    local an = ao and ao.uid or ""
    local bn = bo and bo.uid or ""
    return an < bn
  end)
  local out = {}
  local seen = {}
  for _, edge in ipairs(sorted) do
    local other = derived.symbol_by_uid[direction == "out" and edge.to or edge.from]
    local key = other and (other.name .. "\0" .. other.kind) or tostring(edge)
    -- Do not present a type's own members as external dependencies.
    if other and other.parent_uid == uid then
      -- skip
    elseif not seen[key] then
      seen[key] = true
      out[#out + 1] = edge
      if #out >= (limit or 6) then break end
    end
  end
  return out
end

local function render_symbol(derived, symbol)
  local lines = {}
  local function add(line) lines[#lines + 1] = line end
  add(string.format("## %s — %s", symbol.name, symbol.kind))
  add(string.format("- location: `%s`", symbol_location(symbol)))
  add(string.format("- qualified: `%s`", symbol.qualified))
  add(string.format("- signature: `%s`", symbol.signature or symbol.name))
  if symbol.doc and symbol.doc ~= "" then
    add("- docs: " .. truncate(symbol.doc, 320))
  end
  if symbol.is_type then
    local members = {}
    for _, edge in ipairs(related_edges(derived, symbol.uid, "out", 40)) do
      if edge.kind == "contains" then
        local member = derived.symbol_by_uid[edge.to]
        if member then members[#members + 1] = member.name end
      end
    end
    -- containment edges are excluded from `outgoing`; scan all edges instead
    if #members == 0 then
      for _, edge in ipairs(derived.symbol_edges) do
        if edge.kind == "contains" and edge.from == symbol.uid then
          local member = derived.symbol_by_uid[edge.to]
          if member then members[#members + 1] = member.name end
        end
      end
    end
    if #members > 0 then
      add("- members: " .. table.concat(members, ", ", 1, math.min(#members, 12)))
    end
  end

  local out = related_edges(derived, symbol.uid, "out", 6)
  if #out > 0 then
    local parts = {}
    for _, edge in ipairs(out) do
      local target = derived.symbol_by_uid[edge.to]
      if target then
        parts[#parts + 1] = string.format("`%s` (%s)", target.name, target.kind)
      end
    end
    if #parts > 0 then add("- depends on: " .. table.concat(parts, ", ")) end
  end

  local inc = related_edges(derived, symbol.uid, "in", 6)
  if #inc > 0 then
    local parts = {}
    for _, edge in ipairs(inc) do
      local source = derived.symbol_by_uid[edge.from]
      if source then
        parts[#parts + 1] = string.format("`%s` (%s)", source.name, source.kind)
      end
    end
    if #parts > 0 then add("- used by: " .. table.concat(parts, ", ")) end
  end

  return table.concat(lines, "\n") .. "\n"
end

-- ---------------------------------------------------------------------------
-- Rendering: deps
-- ---------------------------------------------------------------------------

local function module_summary(derived, index, paths)
  local lines = {}
  local function add(line) lines[#lines + 1] = line end
  local namespaces = {}
  for _, path in ipairs(paths) do
    local ns = index.files[path].namespace
    if ns and ns ~= "" then namespaces[ns] = true end
  end
  local ns_list = {}
  for ns in pairs(namespaces) do ns_list[#ns_list + 1] = ns end
  table.sort(ns_list)
  add(string.format("### Module%s", #paths > 1 and (" (" .. #paths .. " files)") or ""))
  if #ns_list > 0 then add("- namespace: `" .. table.concat(ns_list, "`, `") .. "`") end
  for i = 1, math.min(#paths, 6) do
    add(string.format("- `%s`", paths[i]))
  end
  if #paths > 6 then add(string.format("- ... %d more files", #paths - 6)) end

  local import_sources = {}
  local imported_by = {}
  local file_set = {}
  for _, path in ipairs(paths) do file_set[path] = true end
  for _, path in ipairs(paths) do
    local file = index.files[path]
    for i, entry in ipairs(file.imports or {}) do
      local targets = derived.resolved_imports[path][i] or {}
      local suffix = #targets == 0 and " (external)" or ""
      import_sources[entry.source .. suffix] = true
    end
  end
  for _, edge in ipairs(derived.file_edges) do
    if file_set[edge.to] and not file_set[edge.from] then imported_by[edge.from] = true end
  end

  local imports = {}
  for source in pairs(import_sources) do imports[#imports + 1] = source end
  table.sort(imports)
  local importers = {}
  for path in pairs(imported_by) do importers[#importers + 1] = path end
  table.sort(importers)

  add("### Imports")
  if #imports == 0 then
    add("- (none)")
  else
    for i = 1, math.min(#imports, 10) do add("- " .. imports[i]) end
    if #imports > 10 then add(string.format("- ... %d more", #imports - 10)) end
  end
  add("### Imported by")
  if #importers == 0 then
    add("- (none)")
  else
    for i = 1, math.min(#importers, 10) do add("- `" .. importers[i] .. "`") end
    if #importers > 10 then add(string.format("- ... %d more", #importers - 10)) end
  end

  local module_symbols = {}
  for _, symbol in ipairs(derived.symbols) do
    if file_set[symbol.file] and is_key_symbol(symbol) then
      module_symbols[#module_symbols + 1] = symbol
    end
  end
  table.sort(module_symbols, function(a, b)
    if a.score ~= b.score then return a.score > b.score end
    return a.uid < b.uid
  end)
  if #module_symbols > 0 then
    add("### Key symbols")
    for i = 1, math.min(#module_symbols, 8) do
      local symbol = module_symbols[i]
      add(string.format("- `%s` (%s) — `%s`", symbol.name, symbol.kind, symbol_location(symbol)))
    end
  end
  return table.concat(lines, "\n") .. "\n"
end

local function render_symbol_deps(derived, symbol)
  local lines = {}
  local function add(line) lines[#lines + 1] = line end
  add(string.format("## deps: %s — %s", symbol.name, symbol.kind))
  add(string.format("- location: `%s`", symbol_location(symbol)))
  add(string.format("- module: `%s`", symbol.file))

  local out = related_edges(derived, symbol.uid, "out", 8)
  add(string.format("### Outgoing references (%d)", #(derived.outgoing[symbol.uid] or {})))
  if #out == 0 then
    add("- (none)")
  else
    for _, edge in ipairs(out) do
      local target = derived.symbol_by_uid[edge.to]
      if target then
        add(string.format("- -> `%s` (%s) — `%s` [%s]",
          target.name, target.kind, symbol_location(target), edge.kind))
      end
    end
  end

  local inc = related_edges(derived, symbol.uid, "in", 8)
  add(string.format("### Incoming references (%d)", #(derived.incoming[symbol.uid] or {})))
  if #inc == 0 then
    add("- (none)")
  else
    for _, edge in ipairs(inc) do
      local source = derived.symbol_by_uid[edge.from]
      if source then
        add(string.format("- <- `%s` (%s) — `%s` [%s]",
          source.name, source.kind, symbol_location(source), edge.kind))
      end
    end
  end
  return table.concat(lines, "\n") .. "\n"
end

-- ---------------------------------------------------------------------------
-- Index loading / refreshing for read commands
-- ---------------------------------------------------------------------------

local function load_or_refresh(opts)
  opts = opts or {}
  if opts.no_refresh then
    local index = load_json_file(INDEX_PATH)
    if not index then
      io.stderr:write("repo-intel: no index found; run: lua scripts/repo-intel.lua update\n")
      return nil
    end
    if index.version ~= CACHE_VERSION then
      io.stderr:write("repo-intel: cache is from an older version; run: lua scripts/repo-intel.lua update\n")
      return nil
    end
    return index
  end

  local index, stats = refresh_index({})
  if stats and (stats.parsed > 0 or stats.removed > 0) and stats.files > 0 then
    io.stderr:write(string.format("repo-intel: refreshed %d file(s) in %.0f ms\n",
      stats.parsed, (stats.elapsed or 0) * 1000))
  end
  return index
end

-- ---------------------------------------------------------------------------
-- Commands
-- ---------------------------------------------------------------------------

local function cmd_setup(opts)
  print("repo-intel setup " .. SCRIPT_VERSION)
  print("repository: " .. ROOT)

  if not sh_ok("command -v tree-sitter >/dev/null 2>&1") then
    io.stderr:write("tree-sitter CLI not found.\nInstall: xbps-install -Sy tree-sitter-cli\n")
    return 1
  end
  print("tree-sitter: " .. trim(run_capture("tree-sitter --version 2>/dev/null") or "unknown"))

  if not ensure_dir(CACHE_DIR) then
    io.stderr:write("cannot create " .. CACHE_DIR .. "\n")
    return 1
  end
  ensure_ts_config()
  print("config: " .. TS_CONFIG_PATH)

  for _, lang in ipairs(LANG_ORDER) do
    local spec = LANGS[lang]
    local dir = GRAMMAR_DIR .. "/" .. spec.grammar
    if not grammar_available(lang) then
      print("cloning grammar: " .. spec.grammar)
      local ok = sh_ok("git clone --depth 1 --quiet " .. shell_quote(spec.url) .. " " .. shell_quote(dir))
      if not ok then
        io.stderr:write("failed to clone " .. spec.url .. "\n")
        return 1
      end
    else
      print("grammar ok: " .. spec.grammar)
    end
  end

  -- Warm the parser cache (and force a rebuild when --force is set).
  local warm = {}
  for _, path in ipairs(scan_source_files()) do
    local lang = language_for(path)
    if lang and not warm[lang] then warm[lang] = path end
  end
  for _, lang in ipairs(LANG_ORDER) do
    if warm[lang] then
      print("warming parser: " .. lang .. " (" .. warm[lang] .. ")")
      run_tree_sitter(warm[lang], lang, true)
    end
  end

  print("setup complete. Next: lua scripts/repo-intel.lua update")
  return 0
end

local function cmd_update(opts)
  local index, stats = refresh_index({ force = opts.force })
  local derived = derive(index)
  print(string.format("repo-intel update: %d files scanned, %d parsed, %d cached, %d removed, %d failed",
    stats.files, stats.parsed, stats.cached, stats.removed, stats.failed))
  print(string.format("symbols: %d · dependency edges: %d · elapsed: %.0f ms",
    #derived.symbols, #derived.symbol_edges, (stats.elapsed or 0) * 1000))
  local index_size = (lfs.attributes(INDEX_PATH) or {}).size or 0
  local hash_size = (lfs.attributes(HASH_PATH) or {}).size or 0
  print(string.format("cache: %s (%d KB), %s (%d KB)",
    relative_path(INDEX_PATH), math.floor(index_size / 1024 + 0.5),
    relative_path(HASH_PATH), math.floor(hash_size / 1024 + 0.5)))
  return 0
end

local function cmd_overview(opts)
  local index = load_or_refresh(opts)
  if not index then return 1 end
  local derived = derive(index)

  local text
  for _, config in ipairs({
    { symbols = 8, docs = true },
    { symbols = 8, docs = false },
    { symbols = 6, docs = false },
    { symbols = 4, docs = false },
  }) do
    text = build_overview(index, derived, config.symbols, config.docs)
    if estimate_tokens(text, "prose") <= OVERVIEW_BUDGET then break end
  end
  io.write(text)
  return 0
end

local function cmd_map(opts)
  local index = load_or_refresh(opts)
  if not index then return 1 end
  local derived = derive(index)
  local budget = opts.budget or DEFAULT_MAP_BUDGET
  io.write(build_map(derived, budget))
  return 0
end

local function cmd_symbol(opts)
  if not opts.query or opts.query == "" then
    io.stderr:write("usage: lua scripts/repo-intel.lua symbol <name>\n")
    return 1
  end
  local index = load_or_refresh(opts)
  if not index then return 1 end
  local derived = derive(index)
  local matches = symbol_matches(derived, opts.query)
  if #matches == 0 then
    io.stderr:write("repo-intel: no symbol matching '" .. opts.query .. "'\n")
    return 1
  end

  io.write(render_symbol(derived, matches[1]))
  if #matches > 1 then
    local others = {}
    for i = 2, math.min(#matches, 6) do
      local s = matches[i]
      others[#others + 1] = string.format("`%s` (%s, %s)", s.qualified, s.kind, symbol_location(s))
    end
    io.write("\nOther matches: " .. table.concat(others, "; ") .. "\n")
  end
  return 0
end

local function cmd_deps(opts)
  if not opts.query or opts.query == "" then
    io.stderr:write("usage: lua scripts/repo-intel.lua deps <name-or-module>\n")
    return 1
  end
  local index = load_or_refresh(opts)
  if not index then return 1 end
  local derived = derive(index)

  local modules = module_matches(derived, index, opts.query)
  local symbols = symbol_matches(derived, opts.query)

  if #modules > 0 and (looks_like_module(opts.query) or #symbols == 0) then
    io.write("## deps: " .. opts.query .. "\n")
    io.write(module_summary(derived, index, modules))
    if #symbols > 0 then
      io.write("\nRelated symbols: ")
      local names = {}
      for i = 1, math.min(#symbols, 4) do names[#names + 1] = symbols[i].name end
      io.write(table.concat(names, ", ") .. "\n")
    end
    return 0
  end

  if #symbols == 0 then
    io.stderr:write("repo-intel: no symbol or module matching '" .. opts.query .. "'\n")
    return 1
  end
  io.write(render_symbol_deps(derived, symbols[1]))
  if #symbols > 1 then
    io.write(string.format("(%d symbol matches; showing `%s`)\n", #symbols, symbols[1].qualified))
  end
  return 0
end

local function cmd_status(opts)
  local index = load_json_file(INDEX_PATH)
  if not index then
    print("repo-intel: no index yet. Run: lua scripts/repo-intel.lua update")
    return 1
  end
  local symbols, edges = 0, 0
  for _, file in pairs(index.files or {}) do
    symbols = symbols + #(file.symbols or {})
  end
  local derived = derive(index)
  edges = #derived.symbol_edges
  print("repo-intel status " .. SCRIPT_VERSION)
  print("root: " .. ROOT)
  print("cache: " .. CACHE_DIR)
  print(string.format("files: %d · symbols: %d · edges: %d", #derived.paths, symbols, edges))
  print("indexed: " .. tostring(index.generatedAt))
  print("index size: " .. tostring((lfs.attributes(INDEX_PATH) or {}).size or 0) .. " bytes")
  for _, lang in ipairs(LANG_ORDER) do
    print(string.format("grammar %s: %s", lang, grammar_available(lang) and "ok" or "missing (run setup)"))
  end
  local errors = 0
  for path, file in pairs(index.files) do
    if (file.parse_errors or 0) > 0 then
      errors = errors + 1
      print("parse errors: " .. path .. " (" .. tostring(file.parse_errors) .. ")")
    end
  end
  if errors == 0 then print("parse errors: none") end
  return 0
end

-- ---------------------------------------------------------------------------
-- CLI
-- ---------------------------------------------------------------------------

local function usage()
  print([[
repo-intel - deterministic repository intelligence (Lua 5.1)

Usage:
  lua scripts/repo-intel.lua setup                  Clone grammars / warm parsers
  lua scripts/repo-intel.lua update [--force]       Incremental re-index
  lua scripts/repo-intel.lua overview               Markdown architecture overview (<500 tokens)
  lua scripts/repo-intel.lua map [--budget N]       Token-budgeted symbol map (default 1024)
  lua scripts/repo-intel.lua symbol <name>          Signature, docs, location, references
  lua scripts/repo-intel.lua deps <name-or-module>  Incoming/outgoing dependencies
  lua scripts/repo-intel.lua status                 Index/cache/grammar status

Global options:
  --no-refresh    Use the cached index without hash-checking files
  --budget N      Token budget for 'map' (default 1024)
  --force         Force re-parse on 'update' / rebuild parsers on 'setup'
  --help          This message

Cache lives in .repo-intel/ (gitignored). Re-parsing is incremental: only files
whose content hash changed since the last run are sent to tree-sitter.
]])
end

local function parse_cli(argv)
  local command = argv[1]
  local opts = {}
  local positional = {}

  local i = 2
  while i <= #argv do
    local item = argv[i]
    if item == "--no-refresh" then
      opts.no_refresh = true
    elseif item == "--force" then
      opts.force = true
    elseif item == "--help" or item == "-h" then
      opts.help = true
    elseif item == "--budget" then
      opts.budget = tonumber(argv[i + 1])
      i = i + 1
      if not opts.budget then
        io.stderr:write("repo-intel: --budget requires a number\n")
        return nil, nil, 1
      end
    elseif item:match("^%-%-budget=") then
      opts.budget = tonumber(item:match("^%-%-budget=(.+)$"))
      if not opts.budget then
        io.stderr:write("repo-intel: --budget requires a number\n")
        return nil, nil, 1
      end
    elseif item:sub(1, 2) == "--" then
      io.stderr:write("repo-intel: unknown option " .. item .. "\n")
      return nil, nil, 1
    else
      positional[#positional + 1] = item
    end
    i = i + 1
  end

  if opts.budget and opts.budget < 100 then
    io.stderr:write("repo-intel: --budget must be at least 100\n")
    return nil, nil, 1
  end
  opts.query = positional[1]
  return command, opts, 0
end

local function main()
  if arg and (arg[1] == "--help" or arg[1] == "-h") then
    usage()
    return 0
  end
  local command, opts, err = parse_cli(arg or {})
  if err ~= 0 then return err end
  if opts.help or command == "help" or command == nil then
    usage()
    return 0
  end

  if command == "setup" then return cmd_setup(opts) end
  if command == "update" then return cmd_update(opts) end
  if command == "overview" then return cmd_overview(opts) end
  if command == "map" then return cmd_map(opts) end
  if command == "symbol" then return cmd_symbol(opts) end
  if command == "deps" then return cmd_deps(opts) end
  if command == "status" then return cmd_status(opts) end

  io.stderr:write("repo-intel: unknown command '" .. tostring(command) .. "'\n")
  usage()
  return 1
end

os.exit(main())
