/**
 * Storefront browse filters, in display order.
 *
 * `all` and `merch` are filters over the whole catalog; the remaining values match a
 * product's `category`, which the API returns. That value is declared once per document in
 * `zz_tp/documents/<listing>/config.tl` and is the same value a collection page owns — so
 * the chip, the hub page and the sales report all name one shelf, not three.
 *
 * The labels come from the shared link list rather than being repeated here, so the chip
 * row, the navbar, the home-page chips and the hub pages cannot disagree about a name.
 * Physical merch carries no category and is reachable only through `all` and `merch`.
 */
import { HUB_LINKS } from './hubLinks.js'

export const CATEGORIES = [
  { value: 'all', label: 'All' },
  ...HUB_LINKS.map((hub) => ({ value: hub.slug, label: hub.label })),
  { value: 'merch', label: 'Merch' },
]
