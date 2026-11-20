/**
 * Slug and label for each collection page, kept apart from `hubs.js` on purpose.
 *
 * The navbar and the home page only need a name and a URL; the hub copy (about 1,600 words)
 * is read by the build, never by the browser. Keeping the labels in their own module means
 * the SPA bundles nine strings instead of the whole `hubs.js`, while `hubs.js` still imports
 * this list so a label is written once.
 *
 * Order is the order they appear in the navbar menu and the home page chips.
 */
export const HUB_LINKS = [
  { slug: 'kidney', label: 'Kidney & renal' },
  { slug: 'emergency', label: 'Emergency & power outage' },
  { slug: 'moving', label: 'First apartment & moving' },
  { slug: 'budgeting', label: 'Budgeting & money' },
  { slug: 'diet-sheets', label: 'Diet & food lists' },
  { slug: 'meal-planning', label: 'Meal planning & groceries' },
  { slug: 'planners', label: 'Planners & schedules' },
  { slug: 'household', label: 'Home & cleaning' },
  { slug: 'packing', label: 'Packing & travel' },
  { slug: 'baby', label: 'Baby & kids' },
] as const

/**
 * A collection page's slug, for example `'kidney'`.
 *
 * Derived from the list above, so adding a hub is one line and every reader of the union —
 * the filter chips, the navbar, the hub pages — picks the new member up without an edit.
 */
export type HubSlug = (typeof HUB_LINKS)[number]['slug']

/** One shared slug/label pair. */
export type HubLink = (typeof HUB_LINKS)[number]
