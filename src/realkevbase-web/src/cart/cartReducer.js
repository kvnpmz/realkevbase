/**
 * Pure cart state for the RealKevbase store.
 *
 * The cart holds mixed orders: digital PDF downloads (delivered through a 24-hour Azure
 * Blob Storage SAS link) and physical merchandise (which has to be shipped). Every helper
 * here is framework-free so it can be unit-tested without React.
 *
 * Money is handled in integer cents internally to avoid floating point drift
 * (0.1 + 0.2 !== 0.3), and exposed back in major currency units.
 */

/**
 * How a cart line is fulfilled. Mirrors the backend `FulfillmentType` enum.
 * @readonly
 * @enum {string}
 */
export const FULFILLMENT = Object.freeze({
  DIGITAL_PDF: 'DigitalPdf',
  PHYSICAL_MERCH: 'PhysicalMerch',
})

/**
 * @typedef {Object} CartItem
 * @property {string} id Product identifier (matches the backend product GUID).
 * @property {string} name Customer-facing name.
 * @property {number} unitPrice Price for a single unit, in major currency units.
 * @property {'DigitalPdf' | 'PhysicalMerch'} fulfillment How the line is delivered.
 * @property {number} quantity Positive integer.
 * @property {string | null} [pdfBlobName] Blob name for digital PDFs, otherwise null.
 */

/**
 * @typedef {Object} CartTotals
 * @property {number} itemCount Sum of all quantities.
 * @property {number} lineCount Number of distinct lines in the cart.
 * @property {number} subtotal Cart subtotal in major currency units.
 * @property {number} digitalSubtotal Subtotal contributed by PDF downloads.
 * @property {number} merchSubtotal Subtotal contributed by physical merchandise.
 * @property {boolean} hasDigitalPdf Whether the cart contains at least one PDF.
 * @property {boolean} hasPhysicalMerch Whether the cart contains shippable items.
 * @property {boolean} requiresShipping Alias for hasPhysicalMerch, convenient for checkout.
 */

/** @type {{ items: CartItem[] }} */
export const initialCartState = Object.freeze({ items: [] })

/**
 * Converts a major-unit amount to integer cents.
 * @param {number} amount Amount in major units.
 * @returns {number} The amount in cents.
 */
export function toCents(amount) {
  return Math.round(amount * 100)
}

/**
 * Validates and normalizes an untrusted cart item (for example one coming from
 * localStorage or a product card). Throws instead of silently storing bad data.
 *
 * @param {Partial<CartItem>} input Candidate item; `quantity` defaults to 1.
 * @returns {CartItem} A new, fully populated item.
 * @throws {TypeError} When the item is not a valid cart line.
 */
export function normalizeCartItem(input) {
  const raw = input ?? {}
  const id = typeof raw.id === 'string' ? raw.id.trim() : ''
  const name = typeof raw.name === 'string' ? raw.name.trim() : ''
  const unitPrice = Number(raw.unitPrice)
  const quantity = raw.quantity === undefined ? 1 : Number(raw.quantity)

  if (!id) {
    throw new TypeError('Cart item requires a non-empty id.')
  }
  if (!name) {
    throw new TypeError(`Cart item "${id}" requires a non-empty name.`)
  }
  if (!Number.isFinite(unitPrice) || unitPrice < 0) {
    throw new TypeError(`Cart item "${id}" requires a finite, non-negative unitPrice.`)
  }
  if (raw.fulfillment !== FULFILLMENT.DIGITAL_PDF && raw.fulfillment !== FULFILLMENT.PHYSICAL_MERCH) {
    throw new TypeError(
      `Cart item "${id}" requires fulfillment to be ${FULFILLMENT.DIGITAL_PDF} or ${FULFILLMENT.PHYSICAL_MERCH}.`,
    )
  }
  if (!Number.isInteger(quantity) || quantity < 1) {
    throw new TypeError(`Cart item "${id}" requires a positive integer quantity.`)
  }
  if (raw.fulfillment === FULFILLMENT.DIGITAL_PDF && typeof raw.pdfBlobName === 'string' && raw.pdfBlobName.trim() === '') {
    throw new TypeError(`Digital PDF cart item "${id}" cannot have a blank pdfBlobName.`)
  }

  return {
    id,
    name,
    unitPrice,
    fulfillment: raw.fulfillment,
    quantity,
    pdfBlobName: typeof raw.pdfBlobName === 'string' ? raw.pdfBlobName : null,
  }
}

/**
 * Merges an incoming item into the list, summing quantities for the same product.
 * @param {CartItem[]} items Current items.
 * @param {CartItem} incoming Item to add.
 * @returns {CartItem[]} A new list.
 */
function mergeItem(items, incoming) {
  const index = items.findIndex((item) => item.id === incoming.id)
  if (index === -1) {
    return [...items, incoming]
  }

  const existing = items[index]
  // A product id must keep a stable fulfillment type; replace on mismatch rather than
  // creating an ambiguous line.
  if (existing.fulfillment !== incoming.fulfillment) {
    const next = items.slice()
    next[index] = incoming
    return next
  }

  const next = items.slice()
  next[index] = { ...existing, quantity: existing.quantity + incoming.quantity }
  return next
}

/**
 * Reducer for the cart. Unknown actions return the current state unchanged.
 *
 * @param {{ items: CartItem[] }} state Current state.
 * @param {{ type: string, item?: CartItem, id?: string, quantity?: number, items?: CartItem[] }} action
 * @returns {{ items: CartItem[] }} The next state.
 */
export function cartReducer(state, action) {
  switch (action?.type) {
    case 'cart/addItem': {
      if (!action.item) return state
      return { ...state, items: mergeItem(state.items, action.item) }
    }

    case 'cart/removeItem': {
      const items = state.items.filter((item) => item.id !== action.id)
      return items.length === state.items.length ? state : { ...state, items }
    }

    case 'cart/setQuantity': {
      const quantity = Number(action.quantity)
      if (!Number.isFinite(quantity)) return state

      if (quantity <= 0) {
        const items = state.items.filter((item) => item.id !== action.id)
        return items.length === state.items.length ? state : { ...state, items }
      }

      const nextQuantity = Math.max(1, Math.trunc(quantity))
      const items = state.items.map((item) =>
        item.id === action.id ? { ...item, quantity: nextQuantity } : item,
      )
      return { ...state, items }
    }

    case 'cart/clear':
      return state.items.length === 0 ? state : initialCartState

    case 'cart/hydrate': {
      const items = Array.isArray(action.items) ? action.items : []
      return { ...state, items }
    }

    default:
      return state
  }
}

/**
 * Finds a cart line by product id.
 * @param {{ items: CartItem[] }} state Cart state.
 * @param {string} id Product id.
 * @returns {CartItem | undefined} The matching line, if any.
 */
export function selectItem(state, id) {
  return state.items.find((item) => item.id === id)
}

/**
 * Computes totals for a mixed cart.
 * @param {CartItem[]} items Cart items.
 * @returns {CartTotals} Aggregated totals in major currency units.
 */
export function selectTotals(items) {
  let itemCount = 0
  let subtotalCents = 0
  let digitalCents = 0
  let merchCents = 0
  let hasDigitalPdf = false
  let hasPhysicalMerch = false

  for (const item of items) {
    const lineCents = toCents(item.unitPrice) * item.quantity
    itemCount += item.quantity
    subtotalCents += lineCents

    if (item.fulfillment === FULFILLMENT.DIGITAL_PDF) {
      digitalCents += lineCents
      hasDigitalPdf = true
    } else if (item.fulfillment === FULFILLMENT.PHYSICAL_MERCH) {
      merchCents += lineCents
      hasPhysicalMerch = true
    }
  }

  return {
    itemCount,
    lineCount: items.length,
    subtotal: subtotalCents / 100,
    digitalSubtotal: digitalCents / 100,
    merchSubtotal: merchCents / 100,
    hasDigitalPdf,
    hasPhysicalMerch,
    requiresShipping: hasPhysicalMerch,
  }
}
