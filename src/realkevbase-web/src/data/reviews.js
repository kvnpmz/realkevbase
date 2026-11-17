/**
 * Buyer quotes for the landing page.
 *
 * These are real reviews from the shop's Etsy store, quoted as words only — no star row and
 * no review count. Google's review guidelines forbid aggregating ratings from another
 * website into `aggregateRating`, so showing Etsy's stars here would either be an
 * unmarked-up implication of a rating or, if marked up, a violation. They are also kept out
 * of the Product structured data in `scripts/generate-seo.mjs` deliberately: when genuine
 * reviews are collected on this site, those — and only those — can carry markup.
 *
 * Pick quotes about the PDF and what it contains. "Fast delivery" reads as nonsense for a
 * download, and a quote with a caveat belongs in a support reply, not on the storefront.
 */
export const REVIEWS = [
  {
    quote:
      'This is a pretty comprehensive guide for someone who is just starting to learn to eat a low potassium diet. It was quick and easy to download and print for reference.',
    name: 'Mary',
    detail: 'Etsy buyer · August 2026',
  },
  {
    quote: 'Perfectly detailed and organized for my college student’s first apartment.',
    name: 'Liz M.',
    detail: 'Etsy buyer · August 2026',
  },
  {
    quote: 'Chart was easy to read.',
    name: 'Frank P.',
    detail: 'Etsy buyer · September 2026',
  },
]
