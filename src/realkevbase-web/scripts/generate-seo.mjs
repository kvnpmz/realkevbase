#!/usr/bin/env node
/**
 * Build-time search metadata.
 *
 * `vite build` emits one shell and one bundle: every route answers with the same <title>,
 * the same description and an empty <div id="root">, so a crawler has to run the app (and
 * wait for the catalog API) before it can even discover a listing. This pass reads the
 * built shell and writes, beside it:
 *
 *   guides/<slug>.html  the shell with that guide's title, description, canonical URL,
 *                       Open Graph tags and Product JSON-LD in its <head>
 *   sitemap.xml         the home page plus every published guide
 *   robots.txt          crawl rules pointing at the sitemap
 *
 * The www -> apex 301 is a Pages Function (`functions/_middleware.js`, which wrangler
 * bundles from the project root, not from `dist`); it is only checked here so a build
 * cannot ship without it.
 *
 * Everything is derived from the catalog the API serves, so no file can drift from what
 * the storefront actually sells. The shell's `<!-- seo:start -->` block is the template;
 * a shell without those markers means index.html was edited and the build must stop
 * rather than ship pages with the wrong head.
 *
 * Usage: node scripts/generate-seo.mjs [--catalog=<products.json>]
 *
 * Environment: SITE_URL (default https://realkevbase.com),
 *              VITE_API_BASE_URL (default https://api.realkevbase.com).
 */

import { existsSync } from 'node:fs'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import { ACCENT, ACCENT_WASH, SITE_NAME, BRAND } from '../src/data/site.js'
import { HOME_HERO } from '../src/data/home.js'

const HERE = dirname(fileURLToPath(import.meta.url))
const DIST = resolve(HERE, '..', 'dist')
const SHELL_PATH = join(DIST, 'index.html')
const MIDDLEWARE_SOURCE = join(HERE, '..', 'functions', '_middleware.js')

const SITE_URL = (process.env.SITE_URL || 'https://realkevbase.com').replace(/\/+$/, '')
const API_BASE = (process.env.VITE_API_BASE_URL || 'https://api.realkevbase.com').replace(/\/+$/, '')
const FALLBACK_IMAGE = `${SITE_URL}/apple-touch-icon.png`

/**
 * Where the shop states its refund terms. Google's MerchantReturnPolicy markup accepts a
 * link in place of the full policy ("Option B" in its documentation), which is the only
 * honest way to describe a digital download: nothing is posted back, so a return window
 * with a return method would be fiction. Shipping is left out for the same reason — no
 * shipping service exists for a PDF, and a zero-rate zero-day delivery would be a claim
 * about a service that does not exist.
 */
const REFUNDS_URL = `${SITE_URL}/refunds`

/** Slugs become file names and canonical paths, so only lowercase words and dashes pass. */
const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/

const SEO_BLOCK = /<!-- seo:start -->[\s\S]*?<!-- seo:end -->/

/** Escapes text for an HTML attribute or element body. */
function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
}

/** Escapes text for an XML text node (sitemap URLs). */
function escapeXml(value) {
  return String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')
}

/** Serialises JSON-LD without letting a `</script>` inside the data end the script tag. */
function serializeJsonLd(data) {
  return JSON.stringify(data, null, 2).replaceAll('<', '\\u003c')
}

/** Formats the catalog price the way the storefront does. */
function formatPrice(price) {
  const amount = typeof price === 'number' ? price : Number(price)
  if (!Number.isFinite(amount) || amount < 0) {
    throw new Error(`catalog price ${JSON.stringify(price)} is not a usable amount`)
  }
  return amount.toFixed(2)
}

/** Pulls a value straight out of the shell head, so index.html stays the single source. */
function shellHeadValue(shell, pattern, label) {
  const match = shell.match(pattern)
  if (!match) {
    throw new Error(`dist/index.html has no ${label}; the SEO block in index.html is malformed`)
  }
  return match[1]
}

/** Replaces the marked block in the shell with a page-specific head. */
function withSeoBlock(shell, lines) {
  return shell.replace(SEO_BLOCK, `<!-- seo:start -->\n    ${lines.join('\n    ')}\n    <!-- seo:end -->`)
}

/**
 * Normalises one API product into the facts a guide page needs.
 *
 * @param {Record<string, unknown>} product Catalog entry from `GET /api/products`.
 * @returns {{ slug: string, name: string, description: string, price: number, id: string,
 *   cover: string | null, image: string | null } | null} Guide facts, or null for merch.
 */
function toGuide(product) {
  if (!product || product.fulfillment !== 'DigitalPdf' || !product.listingId) return null

  if (typeof product.name !== 'string' || !product.name.trim()) return null

  const slug = String(product.listingId).replaceAll('_', '-').toLowerCase()
  if (!SLUG_PATTERN.test(slug)) {
    throw new Error(`listing id ${JSON.stringify(product.listingId)} does not form a safe URL slug`)
  }

  const thumbnail = typeof product.thumbnailUrl === 'string' ? product.thumbnailUrl : ''
  const pageSizes = Array.isArray(product.pageSizes)
    ? product.pageSizes.filter((size) => typeof size === 'string' && size.trim())
    : []

  return {
    // The untouched API record, baked into the page so the hero can render before the
    // catalog request returns. `normalizeProducts` in the SPA turns it into the same shape
    // the fetch produces.
    raw: product,
    slug,
    id: typeof product.id === 'string' ? product.id : '',
    name: product.name.trim(),
    description: typeof product.description === 'string' ? product.description.trim() : '',
    price: product.price,
    category: typeof product.category === 'string' ? product.category : null,
    pageSizes,
    // The artwork route as the *storefront* serves it: root-relative, same origin, proxied
    // and edge-cached by the Pages Function. The seeded cover, its preload and the app's
    // gallery all have to name the same URL — when the seed asked the API host for the
    // cover while the app asked its own origin, the browser downloaded the same 16KB image
    // twice, once over a second TLS handshake, and the second copy had to finish before
    // the guide could paint its Largest Contentful Paint.
    cover: thumbnail || null,
    // Absolute, because og:image and the Product JSON-LD are read by crawlers that have no
    // idea what "this site" is.
    image: thumbnail.startsWith('/') ? `${API_BASE}${thumbnail}` : thumbnail || null,
  }
}

/**
 * Builds the static product block shown before the bundle has downloaded or run.
 *
 * The storefront cannot paint a product until React boots, which on a phone is most of two
 * seconds of empty screen; a visitor arriving from a social link sees nothing for that long
 * and leaves. This block puts the name, price, paper sizes and cover in the HTML itself, so
 * something real is on screen within a few hundred milliseconds. `src/main.jsx` removes it
 * the instant React is ready, so there is exactly one product on screen at a time.
 *
 * Everything is inline: the block has to render before any stylesheet or script loads, and
 * inline styles cannot be lost to a later cascade.
 *
 * @param {{ name: string, price: number, cover: string | null, pageSizes: string[] }} guide
 *   Facts from the catalog for one guide. `cover` is the same-origin artwork route the app
 *   asks for, so the browser holds one copy of the image instead of two.
 * @returns {string} The seed markup, or an empty string when there is nothing to show.
 */
function guideSeed({ name, price, cover, pageSizes }) {
  if (!name) return ''
  const sizes = pageSizes.length ? pageSizes.join(' + ') : 'PDF'
  // Every thumbnail is a 500x647 webp, so the seed declares that ratio up front: without a
  // reserved box the cover's arrival pushes the name, price and footer down the page, which
  // is pure layout shift on the screen a shopper lands on from a search result.
  //
  // The size matters as much as the ratio. The browser awards Largest Contentful Paint to
  // the biggest thing painted, and the app's gallery panel only exists once the bundle has
  // parsed and the catalog has answered — 4.3s on a throttled phone. A 220px card let that
  // later, larger paint take LCP away from the cover, which is already on screen at ~1.5s;
  // at the width the real hero uses, the seeded cover outranks it and the metric follows
  // the paint a shopper actually sees first.
  const coverMarkup = cover
    ? `<img src="${escapeHtml(cover)}" alt="${escapeHtml(name)} — printable PDF cover" ` +
      'width="400" height="518" fetchpriority="high" decoding="async" ' +
      'style="display:block;width:100%;max-width:400px;height:auto;aspect-ratio:500/647;' +
      'margin:0 auto 20px;border-radius:10px;box-shadow:0 12px 30px rgba(20,22,26,0.16)" />'
    : ''

  return `    <div data-product-seed style="min-height:100vh;box-sizing:border-box;background:#fbfaf7;color:#16181c;` +
    'font:16px/1.55 system-ui,-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif">' +
    '<div style="max-width:44rem;margin:0 auto;padding:32px 20px 8px;text-align:center">' +
    `${coverMarkup}` +
    '<p style="margin:0 0 6px;font-size:13px;letter-spacing:0.08em;text-transform:uppercase;color:#6b6f76">' +
    'SaveSimple by RealKevbase</p>' +
    `<h1 style="margin:0 0 10px;font-size:clamp(1.4rem,5vw,2rem);line-height:1.15;letter-spacing:-0.02em">${escapeHtml(name)}</h1>` +
    `<p style="margin:0;color:#5a6068">$${escapeHtml(formatPrice(price))} · Instant PDF download · ${escapeHtml(sizes)}</p>` +
    '</div></div>\n'
}

/**
 * Builds the static hero that the served HTML shows at `/` before the bundle has run.
 *
 * The home page was the one route with nothing in its body: a crawler that does not
 * execute JavaScript found `<div id="root"></div>` — no <h1>, no copy, nothing to rank —
 * even though the head carried the right title and canonical. The strings come from
 * `src/data/home.js`, the same module `HomePage` renders from, so the seeded hero and the
 * React hero cannot drift; `src/main.jsx` drops the block the moment the bundle boots, so a
 * reader never sees the hero twice. Everything is inline because it has to paint before
 * any stylesheet, script or web font exists.
 *
 * @returns {string} The seeded hero markup.
 */
function homeSeed() {
  const captions = HOME_HERO.captions
    .map((caption) => `<span>${escapeHtml(caption)}</span>`)
    .join('')

  const logo =
    '<span aria-hidden="true" style="width:34px;height:34px;border-radius:10px;display:grid;' +
    'place-items:center;color:#FFFFFF;font-size:13px;font-weight:800;' +
    `letter-spacing:-0.02em;background:${BRAND.gradient}">${escapeHtml(BRAND.initials)}</span>`

  return (
    '      <div data-home-seed style="background:#FBFAF7;color:#14161A;' +
    'font:16px/1.5 system-ui,-apple-system,Segoe UI,Roboto,Helvetica Neue,Arial,sans-serif">' +
    // The bar's height and the hero's padding are the header's and the hero's real metrics,
    // so the page does not jump when React mounts the real ones. They need a <style>
    // element because the header is 65px tall on a phone and 77px from `md` up, and an
    // inline attribute cannot hold a media query.
    '<style>' +
    '[data-home-seed] .rk-bar{display:flex;align-items:center;height:65px;' +
    'border-bottom:1px solid #E7E4DC;background:#FBFAF7}' +
    '[data-home-seed] .rk-bar>div{display:flex;align-items:center;gap:10px;width:100%;' +
    'box-sizing:border-box;max-width:1200px;margin:0 auto;padding:0 16px;font-weight:800;' +
    'font-size:1.05rem}' +
    '[data-home-seed] .rk-hero{background:' +
    `radial-gradient(60% 60% at 72% 0%, ${ACCENT_WASH}, transparent 62%)}` +
    // MUI's Container is 1200px wide *including* its 24px padding, and pads 16px on a
    // phone; the seed copies that box so the text column lands on the same x after boot.
    // The seeded screen is the hero and nothing else. The static footer sits right below
    // it in the HTML, and when React paints the real page the footer is pushed thousands
    // of pixels down; an element that is off-screen both before and after contributes no
    // layout shift, whereas a footer parked in the first viewport scored 0.35 on its own.
    '[data-home-seed] .rk-wrap{box-sizing:border-box;max-width:1200px;margin:0 auto;' +
    'padding:64px 16px 32px;min-height:calc(100vh - 65px)}' +
    '[data-home-seed] .rk-lede{font-size:1.5rem}' +
    '@media (min-width:600px){[data-home-seed] .rk-bar>div{padding:0 24px}' +
    '[data-home-seed] .rk-wrap{padding:64px 24px 32px}}' +
    '@media (min-width:900px){[data-home-seed] .rk-bar{height:77px}' +
    '[data-home-seed] .rk-bar>div{font-size:1.25rem}' +
    '[data-home-seed] .rk-wrap{padding:96px 24px 40px;min-height:calc(100vh - 77px)}}' +
    '</style>' +
    `<div class="rk-bar"><div>${logo}<span>${escapeHtml(BRAND.wordmark)}</span></div></div>` +
    '<div class="rk-hero"><div class="rk-wrap"><div style="max-width:720px">' +
    // MUI's overline is 0.75rem on a 2.66 line box, and the hero Stack leaves 24px after
    // it; both numbers are copied so the headline starts on the same pixel after boot.
    '<p style="margin:0 0 24px;font-size:0.75rem;line-height:2.66;font-weight:700;' +
    `letter-spacing:0.12em;text-transform:uppercase;color:${ACCENT}">` +
    `${escapeHtml(HOME_HERO.overline)}</p>` +
    '<h1 style="margin:0;font-size:clamp(2.6rem,6vw,4.25rem);font-weight:800;line-height:1.03;' +
    `letter-spacing:-0.035em">${escapeHtml(HOME_HERO.title)}</h1>` +
    '<p class="rk-lede" style="margin:24px 0 0;max-width:620px;' +
    `color:#5A6068">${escapeHtml(HOME_HERO.subtitle)}</p>` +
    `<p style="margin:24px 0 0"><a href="${escapeHtml(HOME_HERO.cta.href)}" ` +
    'style="display:inline-block;padding:15px 30px;border-radius:14px;background:#14161A;' +
    `color:#FFFFFF;font-weight:700;text-decoration:none">${escapeHtml(HOME_HERO.cta.label)}</a></p>` +
    '<p style="margin:32px 0 0;display:flex;flex-wrap:wrap;gap:20px;font-size:12px;color:#5A6068">' +
    `${captions}</p>` +
    '</div></div></div></div>\n'
  )
}

/**
 * Puts the home hero inside `#root`.
 *
 * Inside rather than in front of it, unlike the product seed: this block *is* the page, so
 * it stays readable if the bundle fails to load, and React takes the container over on the
 * first render. Guide pages are built from the unseeded shell, so only `/` carries it.
 *
 * @param {string} shell The home page HTML.
 * @returns {string} The page with the hero seeded into `#root`.
 */
function withHomeSeed(shell) {
  const emptyRoot = '<div id="root"></div>'
  if (!shell.includes(emptyRoot)) {
    throw new Error('dist/index.html has no empty <div id="root"></div> to seed the home hero into')
  }
  return shell.replace(emptyRoot, `<div id="root">\n${homeSeed()}    </div>`)
}

/** Loads the catalog from the API, or from a saved JSON file when `--catalog=` is given. */
async function loadCatalog(catalogFile) {
  if (catalogFile) {
    return JSON.parse(await readFile(catalogFile, 'utf8'))
  }

  const url = `${API_BASE}/api/products`
  let response
  try {
    response = await fetch(url, { headers: { Accept: 'application/json' } })
  } catch (cause) {
    throw new Error(
      `could not reach the catalog API at ${url} (${cause.message}); ` +
        'start the API or pass --catalog=<products.json>',
    )
  }

  if (!response.ok) {
    throw new Error(`the catalog API at ${url} answered ${response.status}`)
  }

  return response.json()
}

/** Builds the <head> contents for one guide page. */
function guideHead({ slug, name, description, price, id, category, cover, image }) {
  const url = `${SITE_URL}/guides/${slug}`
  const title = `${name} — ${SITE_NAME}`
  const summary =
    description || `${name}, a printable PDF from the SaveSimple collection by RealKevbase.`
  const imageUrl = image || FALLBACK_IMAGE

  const jsonLd = {
    '@context': 'https://schema.org',
    '@type': 'Product',
    name,
    description: summary,
    url,
    image: [imageUrl],
    sku: id,
    category: category ?? 'Digital PDF',
    brand: { '@type': 'Brand', name: 'RealKevbase' },
    offers: {
      '@type': 'Offer',
      url,
      priceCurrency: 'USD',
      price: formatPrice(price),
      availability: 'https://schema.org/InStock',
      itemCondition: 'https://schema.org/NewCondition',
      hasMerchantReturnPolicy: {
        '@type': 'MerchantReturnPolicy',
        merchantReturnLink: REFUNDS_URL,
      },
    },
  }

  return [
    `<title>${escapeHtml(title)}</title>`,
    `<meta name="description" content="${escapeHtml(summary)}" />`,
    `<link rel="canonical" href="${escapeHtml(url)}" />`,
    `<meta property="og:site_name" content="${escapeHtml(SITE_NAME)}" />`,
    '<meta property="og:type" content="product" />',
    `<meta property="og:title" content="${escapeHtml(title)}" />`,
    `<meta property="og:description" content="${escapeHtml(summary)}" />`,
    `<meta property="og:url" content="${escapeHtml(url)}" />`,
    `<meta property="og:image" content="${escapeHtml(imageUrl)}" />`,
    `<meta property="og:image:alt" content="${escapeHtml(`${name}, first page preview`)}" />`,
    // Pinterest reads these to turn a pin into a Product Pin (live price under the image);
    // og:type=product alone is not enough for the Rich Pin validator.
    //
    // The preload names the root-relative route, not the API host: it has to be the exact
    // URL the app puts in its gallery, or the browser fetches the cover a second time and
    // the Largest Contentful Paint waits for that copy instead of painting from cache.
    cover ? `<link rel="preload" as="image" href="${escapeHtml(cover)}" fetchpriority="high" />` : '',
    `<meta property="product:price:amount" content="${formatPrice(price)}" />`,
    '<meta property="product:price:currency" content="USD" />',
    '<meta name="twitter:card" content="summary_large_image" />',
    `<script type="application/ld+json">${serializeJsonLd(jsonLd)}</script>`,
  ]
}

/** Builds the sitemap with the home page first, then every guide. */
/** Display order and labels for the catalog categories the shop uses. */
const CATEGORIES = [
  ['money-budgeting', 'Money & budgeting'],
  ['healthy-living', 'Healthy living'],
  ['moving-out', 'Moving out'],
  ['guides', 'Everyday guides'],
]

/**
 * File name of the crawlable index, which links every guide from plain HTML. Cloudflare
 * Pages serves it pretty and 308-redirects the `.html` form, so links and the sitemap use
 * INDEX_URL instead of this path.
 */
const INDEX_PATH = 'all-guides.html'

/** Public URL of the index, as Pages actually serves it (no extension, no redirect hop). */
const INDEX_URL = '/all-guides'

/**
 * Static pages that carry the shop's credibility: who runs it, how to reach them, and what
 * happens when an order goes wrong. They live in `public/` rather than the SPA so they add
 * no bundle weight, and they are linked from the footer so a crawler and a first-time buyer
 * both find them. `privacy` shipped before this list existed and was missing from the
 * sitemap until now.
 */
const LEGAL_PAGES = [
  ['/about', 'About'],
  ['/contact', 'Contact'],
  ['/refunds', 'Refunds'],
  ['/terms', 'Terms'],
  ['/privacy', 'Privacy'],
]

/**
 * Builds a standalone, JavaScript-free page listing every guide.
 *
 * The storefront is a single-page app, so the home page carries no <a> elements until
 * React runs. A sitemap tells a crawler the guide URLs exist; this page is what makes them
 * reachable by following links, which is the signal that decides how quickly they are
 * crawled and how much of the catalogue is treated as part of the site.
 *
 * @param {Map<string, ReturnType<typeof toGuide>>} guides Guides keyed by slug.
 * @returns {string} A complete HTML document.
 */
function buildIndexPage(guides) {
  const byCategory = new Map(CATEGORIES.map(([key]) => [key, []]))
  const other = []
  for (const guide of guides.values()) {
    const bucket = byCategory.get(guide.category)
    if (bucket) bucket.push(guide)
    else other.push(guide)
  }

  const section = (label, items) => {
    if (items.length === 0) return ''
    const entries = items
      .sort((a, b) => a.name.localeCompare(b.name))
      .map(
        (guide) =>
          `        <li><a href="/guides/${guide.slug}">${escapeHtml(guide.name)}</a>` +
          `<span>$${escapeHtml(formatPrice(guide.price))}</span></li>`,
      )
      .join('\n')
    return `      <section>\n        <h2>${escapeHtml(label)} <span>${items.length}</span></h2>\n        <ul>\n${entries}\n        </ul>\n      </section>\n`
  }

  const sections =
    CATEGORIES.map(([key, label]) => section(label, byCategory.get(key) ?? [])).join('') +
    section('More guides', other)

  const url = `${SITE_URL}${INDEX_URL}`
  const title = `All ${guides.size} printable guides — ${SITE_NAME}`
  const description = `Every printable PDF in the SaveSimple collection, grouped by subject: money and budgeting, healthy living, moving out and everyday guides. Instant download, $5.`

  return `<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>${escapeHtml(title)}</title>
    <meta name="description" content="${escapeHtml(description)}" />
    <link rel="canonical" href="${escapeHtml(url)}" />
    <meta property="og:site_name" content="${escapeHtml(SITE_NAME)}" />
    <meta property="og:type" content="website" />
    <meta property="og:title" content="${escapeHtml(title)}" />
    <meta property="og:description" content="${escapeHtml(description)}" />
    <meta property="og:url" content="${escapeHtml(url)}" />
    <meta property="og:image" content="${escapeHtml(FALLBACK_IMAGE)}" />
    <style>
      :root { color-scheme: light; }
      body { margin: 0; padding: 40px 20px; background: #fbf9f5; color: #16181c;
        font: 16px/1.6 -apple-system, BlinkMacSystemFont, "Segoe UI", Inter, sans-serif; }
      main { max-width: 900px; margin: 0 auto; }
      h1 { font-size: 32px; line-height: 1.2; margin: 0 0 8px; }
      p.lede { color: #55595f; margin: 0 0 32px; }
      section { margin: 0 0 32px; }
      h2 { font-size: 20px; margin: 0 0 12px; border-bottom: 1px solid #e2ded6; padding-bottom: 8px; }
      h2 span { color: #8a8e94; font-weight: 400; font-size: 15px; }
      ul { list-style: none; margin: 0; padding: 0; }
      li { display: flex; justify-content: space-between; gap: 16px; padding: 7px 0;
        border-bottom: 1px solid #f0ece4; }
      li span { color: #55595f; white-space: nowrap; }
      a { color: inherit; text-decoration: none; border-bottom: 1px solid #cfc9bd; }
      a:hover { border-bottom-color: #16181c; }
      footer { max-width: 900px; margin: 40px auto 0; color: #6b6f76; font-size: 14px; }
    </style>
  </head>
  <body>
    <main>
      <h1>All ${guides.size} printable guides</h1>
      <p class="lede">Every SaveSimple PDF, grouped by subject. Instant download, $5 each.</p>
${sections}    </main>
    <footer><a href="/">Back to the shop</a></footer>
  </body>
</html>
`
}

/**
 * Puts the static product block in front of the app root.
 *
 * It sits outside `#root` so React's first render cannot wipe it before there is a product
 * to replace it with; `src/main.jsx` removes the block itself once the bundle is running.
 * The home page and the index get no seed, because neither shows one product.
 *
 * @param {string} shell Page HTML to extend.
 * @param {{ name: string, price: number, image: string | null, pageSizes: string[] }} guide
 *   Facts for the product this page sells.
 * @returns {string} The page with the seed inserted before `#root`.
 */
function withProductSeed(shell, guide) {
  const seed = guideSeed(guide)
  if (!seed) return shell
  return shell.replace('<div id="root">', `${seed}    <div id="root">`)
}

/**
 * Bakes one guide's catalog record into its page as JSON.
 *
 * The SPA reads it through `readInlineProduct` and renders the hero without waiting for
 * `GET /api/products`, which on a throttled phone is a second of TLS before the first byte
 * and was the last thing standing between the page and a green Largest Contentful Paint.
 * `type="application/json"` keeps it inert — the browser will not execute or display it —
 * and the escape in `serializeJsonLd` makes a `</script>` inside the data harmless.
 *
 * @param {string} shell Page HTML to extend.
 * @param {Record<string, unknown>} guide Guide facts, including the raw catalog record.
 * @returns {string} The page with the record inserted before `#root`.
 */
function withInlineProduct(shell, guide) {
  if (!guide.raw) return shell
  // Compact, unlike the pretty-printed JSON-LD: this rides in the page body, where the
  // indentation would be shipped bytes for nobody's benefit. `<` is still escaped.
  const payload = JSON.stringify({ slug: guide.slug, product: guide.raw }).replaceAll('<', '\\u003c')
  const tag = `    <script type="application/json" id="guide-product">${payload}</script>\n`
  return shell.replace('<div id="root">', `${tag}    <div id="root">`)
}

/**
 * Appends a link to the index in plain HTML, outside the app root, so a crawler reaches
 * the catalogue from the home page and from every guide without running JavaScript.
 *
 * @param {string} shell Page HTML to extend.
 * @returns {string} The page with the footer added once.
 */
function withIndexFooter(shell) {
  if (shell.includes('data-seo-index')) return shell
  const footer =
    '    <footer data-seo-index style="padding:28px 20px;text-align:center;' +
    'font:14px/1.5 -apple-system,BlinkMacSystemFont,Segoe UI,Inter,sans-serif;color:#6b6f76;' +
    'border-top:1px solid #e8e3d9;background:#fbf9f5">' +
    `<a href="${INDEX_URL}" style="color:#16181c;text-decoration:none;` +
    'border-bottom:1px solid #cfc9bd">Browse all printable guides</a>' +
    '<p style="margin:12px 0 0;font-size:13px;color:#5A6068">' +
    LEGAL_PAGES.map(
      ([path, label]) => `<a href="${path}" style="color:#6b6f76;text-decoration:none">${label}</a>`,
    ).join(' · ') +
    '</p></footer>\n'
  return shell.replace('</body>', `${footer}  </body>`)
}

function buildSitemap(slugs, lastmod) {
  const urls = [
    `${SITE_URL}/`,
    `${SITE_URL}${INDEX_URL}`,
    ...LEGAL_PAGES.map(([path]) => `${SITE_URL}${path}`),
    ...slugs.map((slug) => `${SITE_URL}/guides/${slug}`),
  ]
  const entries = urls
    .map(
      (url) =>
        `  <url>\n    <loc>${escapeXml(url)}</loc>\n    <lastmod>${lastmod}</lastmod>\n  </url>`,
    )
    .join('\n')

  return (
    '<?xml version="1.0" encoding="UTF-8"?>\n' +
    '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n' +
    `${entries}\n</urlset>\n`
  )
}

const ROBOTS = `# Every guide is prerendered, so a crawler sees titles, canonicals and the
# catalog without running JavaScript. The sitemap is generated from the API catalog.
User-agent: *
Allow: /

Sitemap: ${SITE_URL}/sitemap.xml
`

async function main() {
  const catalogFile = process.argv
    .slice(2)
    .find((argument) => argument.startsWith('--catalog='))
    ?.slice('--catalog='.length)

  const shell = await readFile(SHELL_PATH, 'utf8')
  if (!SEO_BLOCK.test(shell)) {
    throw new Error('dist/index.html has no <!-- seo:start --> block to rewrite')
  }

  const homeTitle = shellHeadValue(shell, /<title>([\s\S]*?)<\/title>/, '<title>')
  const homeDescription = shellHeadValue(
    shell,
    /<meta name="description" content="([^"]*)"/,
    'description meta tag',
  )

  const catalog = await loadCatalog(catalogFile)
  if (!Array.isArray(catalog)) {
    throw new Error('the catalog response was not an array')
  }

  const guides = new Map()
  for (const product of catalog) {
    const guide = toGuide(product)
    if (!guide) continue
    if (guides.has(guide.slug)) {
      throw new Error(`two catalog entries claim /guides/${guide.slug}`)
    }
    guides.set(guide.slug, guide)
  }

  if (guides.size === 0) {
    throw new Error('the catalog returned no digital listings to prerender')
  }

  const homeUrl = `${SITE_URL}/`
  const homeShell = withSeoBlock(shell, [
    `<title>${homeTitle}</title>`,
    `<meta name="description" content="${homeDescription}" />`,
    `<link rel="canonical" href="${escapeHtml(homeUrl)}" />`,
    `<meta property="og:site_name" content="${escapeHtml(SITE_NAME)}" />`,
    '<meta property="og:type" content="website" />',
    `<meta property="og:title" content="${homeTitle}" />`,
    `<meta property="og:description" content="${homeDescription}" />`,
    `<meta property="og:url" content="${escapeHtml(homeUrl)}" />`,
    `<meta property="og:image" content="${escapeHtml(FALLBACK_IMAGE)}" />`,
    '<meta name="twitter:card" content="summary_large_image" />',
  ])

  await writeFile(SHELL_PATH, withIndexFooter(withHomeSeed(homeShell)))
  await writeFile(join(DIST, INDEX_PATH), buildIndexPage(guides))
  await mkdir(join(DIST, 'guides'), { recursive: true })
  await Promise.all(
    [...guides.values()].map((guide) =>
      writeFile(
        join(DIST, 'guides', `${guide.slug}.html`),
        withIndexFooter(withInlineProduct(withProductSeed(withSeoBlock(shell, guideHead(guide)), guide), guide)),
      ),
    ),
  )

  await writeFile(join(DIST, 'sitemap.xml'), buildSitemap([...guides.keys()], new Date().toISOString().slice(0, 10)))
  await writeFile(join(DIST, 'robots.txt'), ROBOTS)

  if (!existsSync(MIDDLEWARE_SOURCE)) {
    throw new Error('functions/_middleware.js is missing, so www would not redirect to the apex')
  }

  console.log(
    `seo: prerendered ${guides.size} guide pages plus the all-guides index, sitemap with ` +
      `${guides.size + 2 + LEGAL_PAGES.length} URLs, ` +
      'robots.txt and the www redirect middleware',
  )
}

main().catch((error) => {
  console.error(`seo: ${error.message}`)
  process.exit(1)
})
