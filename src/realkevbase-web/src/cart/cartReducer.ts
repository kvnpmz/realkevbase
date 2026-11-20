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
 */
export const FULFILLMENT = Object.freeze({
  DIGITAL_PDF: 'DigitalPdf',
  PHYSICAL_MERCH: 'PhysicalMerch',
})

/** The two delivery types a product can have. */
export type Fulfillment = (typeof FULFILLMENT)[keyof typeof FULFILLMENT]

/** One line in the cart. */
export interface CartItem {
  /** Product identifier (matches the backend product GUID). */
  id: string
  /** Customer-facing name. */
  name: string
  /** Price for a single unit, in major currency units. */
  unitPrice: number
  /** How the line is delivered. */
  fulfillment: Fulfillment
  /** Positive integer. */
  quantity: number
  /** Blob name for digital PDFs, otherwise null. */
  pdfBlobName?: string | null
}

/** Aggregated figures for a mixed cart. */
export interface CartTotals {
  /** Sum of all quantities. */
  itemCount: number
  /** Number of distinct lines in the cart. */
  lineCount: number
  /** Cart subtotal in major currency units. */
  subtotal: number
  /** Subtotal contributed by PDF downloads. */
  digitalSubtotal: number
  /** Subtotal contributed by physical merchandise. */
  merchSubtotal: number
  /** Whether the cart contains at least one PDF. */
  hasDigitalPdf: boolean
  /** Whether the cart contains shippable items. */
  hasPhysicalMerch: boolean
  /** Alias for hasPhysicalMerch, convenient for checkout. */
  requiresShipping: boolean
}

/** Cart state as the reducer holds it. */
export interface CartState {
  items: CartItem[]
}

/** Everything the reducer understands; anything else leaves the state untouched. */
export type CartAction =
  | { type: 'cart/addItem'; item: CartItem }
  | { type: 'cart/removeItem'; id: string }
  | { type: 'cart/setQuantity'; id: string; quantity: number }
  | { type: 'cart/clear' }
  | { type: 'cart/hydrate'; items: CartItem[] }

export const initialCartState: CartState = Object.freeze({ items: [] })

/**
 * Converts a major-unit amount to integer cents.
 * @param amount Amount in major units.
 * @returns The amount in cents.
 */
export function toCents(amount: number): number {
  return Math.round(amount * 100)
}

/** True when the value is one of the two delivery types. */
function isFulfillment(value: unknown): value is Fulfillment {
  return value === FULFILLMENT.DIGITAL_PDF || value === FULFILLMENT.PHYSICAL_MERCH
}

/** True for a plain object, so untrusted input can be read field by field. */
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

/**
 * Validates and normalizes an untrusted cart item (for example one coming from
 * localStorage or a product card). Throws instead of silently storing bad data.
 *
 * @param input Candidate item; `quantity` defaults to 1.
 * @returns A new, fully populated item.
 * @throws {TypeError} When the item is not a valid cart line.
 */
export function normalizeCartItem(input: unknown): CartItem {
  const raw = isRecord(input) ? input : {}
  const id = typeof raw.id === 'string' ? raw.id.trim() : ''
  const name = typeof raw.name === 'string' ? raw.name.trim() : ''
  const unitPrice = Number(raw.unitPrice)
  const quantity = raw.quantity === undefined ? 1 : Number(raw.quantity)
  const pdfBlobName = typeof raw.pdfBlobName === 'string' ? raw.pdfBlobName : null

  if (!id) {
    throw new TypeError('Cart item requires a non-empty id.')
  }
  if (!name) {
    throw new TypeError(`Cart item "${id}" requires a non-empty name.`)
  }
  if (!Number.isFinite(unitPrice) || unitPrice < 0) {
    throw new TypeError(`Cart item "${id}" requires a finite, non-negative unitPrice.`)
  }
  if (!isFulfillment(raw.fulfillment)) {
    throw new TypeError(
      `Cart item "${id}" requires fulfillment to be ${FULFILLMENT.DIGITAL_PDF} or ${FULFILLMENT.PHYSICAL_MERCH}.`,
    )
  }
  if (!Number.isInteger(quantity) || quantity < 1) {
    throw new TypeError(`Cart item "${id}" requires a positive integer quantity.`)
  }
  if (raw.fulfillment === FULFILLMENT.DIGITAL_PDF && pdfBlobName !== null && pdfBlobName.trim() === '') {
    throw new TypeError(`Digital PDF cart item "${id}" cannot have a blank pdfBlobName.`)
  }

  return {
    id,
    name,
    unitPrice,
    fulfillment: raw.fulfillment,
    quantity,
    pdfBlobName,
  }
}

/**
 * Merges an incoming item into the list, summing quantities for the same product.
 * @param items Current items.
 * @param incoming Item to add.
 * @returns A new list.
 */
function mergeItem(items: CartItem[], incoming: CartItem): CartItem[] {
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
 */
export function cartReducer(state: CartState, action: CartAction): CartState {
  switch (action?.type) {
    case 'cart/addItem':
      // The dispatch sites are typed, but a plain object from JS or a future caller can
      // still arrive without an item; ignore it rather than dereferencing undefined.
      if (!action.item) return state
      return { ...state, items: mergeItem(state.items, action.item) }

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
 * @param state Cart state.
 * @param id Product id.
 * @returns The matching line, if any.
 */
export function selectItem(state: CartState, id: string): CartItem | undefined {
  return state.items.find((item) => item.id === id)
}

/**
 * Computes totals for a mixed cart.
 * @param items Cart items.
 * @returns Aggregated totals in major currency units.
 */
export function selectTotals(items: readonly CartItem[]): CartTotals {
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
