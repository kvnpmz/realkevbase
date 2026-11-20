/**
 * Formats a major-unit amount as currency for display.
 * @param amount Amount in major currency units (e.g. 19.99).
 * @param currency ISO 4217 currency code.
 * @returns Localized currency string.
 */
export function formatMoney(amount: number, currency = 'USD'): string {
  const value = Number.isFinite(amount) ? amount : 0
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency,
  }).format(value)
}
