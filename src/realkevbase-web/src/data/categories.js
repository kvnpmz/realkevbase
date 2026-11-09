/**
 * Storefront browse filters, in display order.
 *
 * `all` and `merch` are filters over the whole catalog; the remaining values match a
 * product's `category`, which the API returns. Physical merch carries no category and is
 * therefore reachable only through `all` and `merch`.
 *
 * The product data itself lives in the API (`GET /api/products`); this module only describes
 * how those products are grouped in the UI.
 */
export const CATEGORIES = [
  { value: 'all', label: 'All' },
  { value: 'moving-out', label: 'Moving Out' },
  { value: 'healthy-living', label: 'Healthy Living' },
  { value: 'money-budgeting', label: 'Money & Budgeting' },
  { value: 'guides', label: 'Guides' },
  { value: 'merch', label: 'Merch' },
]
