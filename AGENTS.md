# Global Collaboration & Workflow Rules

- Commit changes after completing each self-contained task or file implementation.
- Do not modify files or configurations outside the requested task scope.
- Never write hardcoded secret keys; rely on configuration/environment settings.
- Maintain XML documentation comments on public interfaces and services.

# Tool Usage Rules
- Always try `search_code` before reading files or grepping; only fall back to `rg` for exact strings and read files only after a search points to them.

## Repository Intelligence (repo-intel)

Use the local deterministic index before grep or reading whole files:

- Session start: `lua scripts/repo-intel.lua overview` (architecture summary, <500 tokens).
- Symbol lookup: `lua scripts/repo-intel.lua symbol <Name>` (signature, docs, location, line range).
- Dependency lookup: `lua scripts/repo-intel.lua deps <Name-or-Module>` (incoming/outgoing).
- Budgeted map: `lua scripts/repo-intel.lua map --budget 1024`.
- After edits: `lua scripts/repo-intel.lua update` (hash-incremental; only changed files are re-parsed).

Cache lives in `.repo-intel/` and is gitignored. `--no-refresh` uses the cache as-is.
If grammars or the index are missing, run `lua scripts/repo-intel.lua setup`.
