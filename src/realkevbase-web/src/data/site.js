/** Brand name, used in guide titles and share cards. */
export const SITE_NAME = 'SaveSimple by RealKevbase'

/** Browser-tab title for the storefront landing page. */
export const SITE_TITLE = 'SaveSimple by RealKevbase — checklists, planners & printables'

/**
 * The header's logo tile and wordmark.
 *
 * Shared with `scripts/generate-seo.mjs`, which seeds the same bar into the served HTML so
 * the hero does not jump down when React mounts the real header. The gradient stays here
 * rather than in the Navbar so the two bars cannot drift into different blues.
 */
export const BRAND = {
  initials: 'RK',
  wordmark: 'RealKevbase',
  gradient: 'linear-gradient(135deg, #2F6BFF 0%, #1B44C8 100%)',
}
