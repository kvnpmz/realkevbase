/** Brand name, used in guide titles and share cards. */
export const SITE_NAME = 'SaveSimple by RealKevbase'

/** Browser-tab title for the storefront landing page. */
export const SITE_TITLE = 'SaveSimple by RealKevbase — checklists, planners & printables'

/**
 * The one accent blue.
 *
 * It was `#2F6BFF`, which measures 4.34:1 on the off-white canvas — just under WCAG AA's
 * 4.5 for the 12px overlines it paints, and 4.49 for white text on it. One step darker
 * gives 7.5:1 and 7.8:1, so the colour still reads as the same blue but stops failing
 * Lighthouse's contrast audit. The theme, the logo tile and the prerendered hero all read
 * this constant rather than restating a hex.
 */
export const ACCENT = '#1B44C8'

/** The accent as a tint, for the hero wash and the text-selection highlight. */
export const ACCENT_WASH = 'rgba(27, 68, 200, 0.12)'

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
  gradient: `linear-gradient(135deg, ${ACCENT} 0%, #14339A 100%)`,
}
