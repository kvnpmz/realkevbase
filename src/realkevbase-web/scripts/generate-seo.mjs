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

import { SITE_NAME } from '../src/data/site.js'

const HERE = dirname(fileURLToPath(import.meta.url))
const DIST = resolve(HERE, '..', 'dist')
const SHELL_PATH = join(DIST, 'index.html')
const MIDDLEWARE_SOURCE = join(HERE, '..', 'functions', '_middleware.js')

const SITE_URL = (process.env.SITE_URL || 'https://realkevbase.com').replace(/\/+$/, '')
const API_BASE = (process.env.VITE_API_BASE_URL || 'https://api.realkevbase.com').replace(/\/+$/, '')
const FALLBACK_IMAGE = `${SITE_URL}/apple-touch-icon.png`

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
 *   image: string | null } | null} Guide facts, or null for merch.
 */
function toGuide(product) {
  if (!product || product.fulfillment !== 'DigitalPdf' || !product.listingId) return null
  if (typeof product.name !== 'string' || !product.name.trim()) return null

  const slug = String(product.listingId).replaceAll('_', '-').toLowerCase()
  if (!SLUG_PATTERN.test(slug)) {
    throw new Error(`listing id ${JSON.stringify(product.listingId)} does not form a safe URL slug`)
  }

  const thumbnail = typeof product.thumbnailUrl === 'string' ? product.thumbnailUrl : ''
  return {
    slug,
    id: typeof product.id === 'string' ? product.id : '',
    name: product.name.trim(),
    description: typeof product.description === 'string' ? product.description.trim() : '',
    price: product.price,
    category: typeof product.category === 'string' ? product.category : null,
    image: thumbnail.startsWith('/') ? `${API_BASE}${thumbnail}` : thumbnail || null,
  }
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
function guideHead({ slug, name, description, price, id, category, image }) {
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
    '<meta name="twitter:card" content="summary_large_image" />',
    `<script type="application/ld+json">${serializeJsonLd(jsonLd)}</script>`,
  ]
}

/** Builds the sitemap with the home page first, then every guide. */
function buildSitemap(slugs, lastmod) {
  const urls = [`${SITE_URL}/`, ...slugs.map((slug) => `${SITE_URL}/guides/${slug}`)]
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

  await writeFile(SHELL_PATH, homeShell)
  await mkdir(join(DIST, 'guides'), { recursive: true })
  await Promise.all(
    [...guides.values()].map((guide) =>
      writeFile(join(DIST, 'guides', `${guide.slug}.html`), withSeoBlock(shell, guideHead(guide))),
    ),
  )

  await writeFile(join(DIST, 'sitemap.xml'), buildSitemap([...guides.keys()], new Date().toISOString().slice(0, 10)))
  await writeFile(join(DIST, 'robots.txt'), ROBOTS)

  if (!existsSync(MIDDLEWARE_SOURCE)) {
    throw new Error('functions/_middleware.js is missing, so www would not redirect to the apex')
  }

  console.log(
    `seo: prerendered ${guides.size} guide pages, sitemap with ${guides.size + 1} URLs, ` +
      'robots.txt and the www redirect middleware',
  )
}

main().catch((error) => {
  console.error(`seo: ${error.message}`)
  process.exit(1)
})
