import { useCallback, useMemo, useState } from 'react'

import { useCart } from '../hooks/useCart.js'
import { CartContext } from './cartContext.js'

/**
 * Provides one cart instance to the whole storefront and opens the drawer when an item is
 * added, so every "Add to cart" click has immediate, visible feedback.
 *
 * @param {{ children: React.ReactNode }} props Provider children.
 * @returns {React.JSX.Element} The context provider.
 */
export function CartProvider({ children }) {
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
    (item, quantity = 1) => {
      addToCart(item, quantity)
      setIsCartOpen(true)
    },
    [addToCart],
  )

  const startCheckout = useCallback(
    async (options) => {
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
