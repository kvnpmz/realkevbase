import { createContext, useContext } from 'react'

import type { CheckoutSession } from '../api/checkoutApi'
import type { CheckoutStatus } from '../hooks/useCart'
import type { CartItem, CartTotals } from './cartReducer'

/**
 * Context value shared by the navbar, product grid and cart drawer.
 * It is the `useCart` result plus drawer visibility helpers.
 */
export interface CartContextValue {
  items: CartItem[]
  totals: CartTotals
  isEmpty: boolean
  addItem: (item: Omit<CartItem, 'quantity'>, quantity?: number) => void
  removeItem: (id: string) => void
  updateQuantity: (id: string, quantity: number) => void
  increment: (id: string) => void
  decrement: (id: string) => void
  clearCart: () => void
  checkout: (options?: { signal?: AbortSignal }) => Promise<CheckoutSession | null>
  checkoutStatus: CheckoutStatus
  checkoutError: Error | null
  checkoutSession: CheckoutSession | null
  resetCheckout: () => void
  isCartOpen: boolean
  openCart: () => void
  closeCart: () => void
}

export const CartContext = createContext<CartContextValue | null>(null)

/**
 * Access the shared cart. Must be used inside <CartProvider>.
 * @returns The cart context value.
 * @throws {Error} When called outside a CartProvider.
 */
export function useCartContext(): CartContextValue {
  const context = useContext(CartContext)
  if (!context) {
    throw new Error('useCartContext must be used within a CartProvider.')
  }
  return context
}
