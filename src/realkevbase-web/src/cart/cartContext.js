import { createContext, useContext } from 'react'

/**
 * Context value shared by the navbar, product grid and cart drawer.
 * It is the `useCart` result plus drawer visibility helpers.
 *
 * @typedef {Object} CartContextValue
 * @property {import('./cartReducer.js').CartItem[]} items
 * @property {import('./cartReducer.js').CartTotals} totals
 * @property {boolean} isEmpty
 * @property {(item: Omit<import('./cartReducer.js').CartItem, 'quantity'>, quantity?: number) => void} addItem
 * @property {(id: string) => void} removeItem
 * @property {(id: string, quantity: number) => void} updateQuantity
 * @property {(id: string) => void} increment
 * @property {(id: string) => void} decrement
 * @property {() => void} clearCart
 * @property {(options?: { signal?: AbortSignal }) => Promise<import('../api/checkoutApi.js').CheckoutSession | null>} checkout
 * @property {string} checkoutStatus
 * @property {Error | null} checkoutError
 * @property {import('../api/checkoutApi.js').CheckoutSession | null} checkoutSession
 * @property {() => void} resetCheckout
 * @property {boolean} isCartOpen
 * @property {() => void} openCart
 * @property {() => void} closeCart
 */

/** @type {React.Context<CartContextValue | null>} */
export const CartContext = createContext(null)

/**
 * Access the shared cart. Must be used inside <CartProvider>.
 * @returns {CartContextValue} The cart context value.
 * @throws {Error} When called outside a CartProvider.
 */
export function useCartContext() {
  const context = useContext(CartContext)
  if (!context) {
    throw new Error('useCartContext must be used within a CartProvider.')
  }
  return context
}
