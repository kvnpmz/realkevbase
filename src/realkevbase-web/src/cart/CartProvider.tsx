import { useCallback, useMemo, useState } from 'react'
import type { ReactNode } from 'react'

import type { CheckoutSession } from '../api/checkoutApi'
import type { CartItem } from './cartReducer'
import { useCart } from '../hooks/useCart'
import { CartContext } from './cartContext'

/** The provider takes the app's children and nothing else. */
export interface CartProviderProps {
  children: ReactNode
}

/**
 * Provides one cart instance to the whole storefront and opens the drawer when an item is
 * added, so every "Add to cart" click has immediate, visible feedback.
 */
export function CartProvider({ children }: CartProviderProps) {
  const {
    items,
    totals,
    isEmpty,
    addItem: addToCart,
    removeItem,
    updateQuantity,
    increment,
    decrement,
    clearCart,
    checkout,
    checkoutStatus,
    checkoutError,
    checkoutSession,
    resetCheckout,
  } = useCart()

  const [isCartOpen, setIsCartOpen] = useState(false)

  const openCart = useCallback(() => setIsCartOpen(true), [])
  const closeCart = useCallback(() => setIsCartOpen(false), [])

  const addItem = useCallback(
    (item: Omit<CartItem, 'quantity'>, quantity = 1) => {
      addToCart(item, quantity)
      setIsCartOpen(true)
    },
    [addToCart],
  )

  const startCheckout = useCallback(
    async (options?: { signal?: AbortSignal }): Promise<CheckoutSession | null> => {
      const session = await checkout(options)
      // With a redirect URL the browser is leaving for Stripe; otherwise let the
      // consumer drive Stripe.js from a closed drawer.
      if (session && !session.url) setIsCartOpen(false)
      return session
    },
    [checkout],
  )

  const value = useMemo(
    () => ({
      items,
      totals,
      isEmpty,
      addItem,
      removeItem,
      updateQuantity,
      increment,
      decrement,
      clearCart,
      checkout: startCheckout,
      checkoutStatus,
      checkoutError,
      checkoutSession,
      resetCheckout,
      isCartOpen,
      openCart,
      closeCart,
    }),
    [
      items,
      totals,
      isEmpty,
      addItem,
      removeItem,
      updateQuantity,
      increment,
      decrement,
      clearCart,
      startCheckout,
      checkoutStatus,
      checkoutError,
      checkoutSession,
      resetCheckout,
      isCartOpen,
      openCart,
      closeCart,
    ],
  )

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>
}

export default CartProvider
