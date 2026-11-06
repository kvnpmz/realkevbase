/**
 * Formats a major-unit amount as currency for display.
 * @param {number} amount Amount in major currency units (e.g. 19.99).
 * @param {string} [currency] ISO 4217 currency code.
 * @returns {string} Localized currency string.
 */
export function formatMoney(amount, currency = 'USD') {
  const value = Number.isFinite(amount) ? amount : 0
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency,
  }).format(value)
}
