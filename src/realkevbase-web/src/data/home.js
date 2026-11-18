/**
 * Home hero copy, kept out of the component so it can be prerendered too.
 *
 * `HomePage` paints this after the bundle parses, which on a phone is most of a second of
 * empty canvas; `scripts/generate-seo.mjs` copies the same strings into the served HTML for
 * `/`, so the <h1> exists for a crawler that never runs JavaScript and for a visitor still
 * waiting on the bundle. Two copies of this paragraph would drift within a week, so both
 * readers import these strings instead of restating them.
 *
 * No prices, counts or niche names appear here on purpose: the collection grows, and copy
 * that dates itself is worse than copy that stays true.
 */
export const HOME_HERO = {
  overline: 'The SaveSimple collection',
  title: 'Printable checklists and planners, made effortless.',
  subtitle:
    'One-page PDFs you download the moment you pay and print at home. Checklists, planners ' +
    'and trackers for the things you keep meaning to get to.',
  cta: { label: 'Shop the collection', href: '#products' },
  captions: ['24-hour download links', 'Stripe checkout', 'Ships worldwide'],
}
