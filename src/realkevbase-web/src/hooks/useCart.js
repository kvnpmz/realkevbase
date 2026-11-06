/**
 * useCart — shopping cart state and Stripe checkout orchestration.
 *
 * Supports mixed orders (digital PDFs + physical merchandise), keeps totals in sync,
 * persists the cart to localStorage, and talks to the backend `/api/checkout` route group
 * to create a Stripe Checkout Session. No UI is rendered here: the hook returns state and
 * actions for whatever components consume it.
 *
 * Usage:
 *   const { items, totals, addItem, checkout, checkoutStatus } = useCart()
 */

import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from 'react'

import { checkoutApi as defaultCheckoutApi } from '../api/checkoutApi.js'
import {
  initialCartState,
  normalizeCartItem,
  cartReducer,
  selectTotals,
} from '../cart/cartReducer.js'
import {
  DEFAULT_CART_STORAGE_KEY,
  getDefaultStorage,
  readStoredCart,
  writeStoredCart,
} from '../cart/cartStorage.js'

/**
 * Lifecycle of a checkout attempt.
 * @readonly
 * @enum {string}
 */
export const CHECKOUT_STATUS = Object.freeze({
  /** Nothing submitted yet, or the last attempt was reset. */
  IDLE: 'idle',
  /** The Stripe session request is in flight. */
  SUBMITTING: 'submitting',
  /** A session was created and the browser is being sent to Stripe. */
  REDIRECTING: 'redirecting',
  /** The request failed; see `checkoutError`. */
  ERROR: 'error',
})

/**
 * Default redirect implementation: leave the SPA for Stripe's hosted page.
 * @param {string} url Stripe Checkout URL.
 * @returns {void}
 */
function defaultNavigate(url) {
  window.location.assign(url)
}

/**
 * @typedef {Object} UseCartOptions
 * @property {ReturnType<typeof import('../api/checkoutApi').createCheckoutApi>} [api]
 *   Checkout client override (defaults to the shared client).
 * @property {(url: string) => void} [navigate] Redirect function override (defaults to window.location.assign).
 * @property {string} [storageKey] localStorage key for the cart.
 * @property {boolean} [persist] Set false to disable persistence.
 * @property {Storage | null} [storage] Storage override, mainly for tests.
 * @property {import('../cart/cartReducer').CartItem[]} [initialItems] Seed items instead of reading storage.
 */

/**
 * @typedef {Object} UseCartResult
 * @property {import('../cart/cartReducer').CartItem[]} items Current cart lines.
 * @property {import('../cart/cartReducer').CartTotals} totals Aggregated totals.
 * @property {boolean} isEmpty Whether the cart holds no lines.
 * @property {(item: Omit<import('../cart/cartReducer').CartItem, 'quantity'>, quantity?: number) => void} addItem
 * @property {(id: string) => void} removeItem
 * @property {(id: string, quantity: number) => void} updateQuantity
 * @property {(id: string) => void} increment
 * @property {(id: string) => void} decrement
 * @property {() => void} clearCart
 * @property {(options?: { signal?: AbortSignal }) => Promise<import('../api/checkoutApi').CheckoutSession | null>} checkout
 * @property {string} checkoutStatus One of CHECKOUT_STATUS.
 * @property {Error | null} checkoutError Last checkout failure (a CheckoutApiError when the API responded).
 * @property {import('../api/checkoutApi').CheckoutSession | null} checkoutSession Most recent session.
 * @property {() => void} resetCheckout Clear checkout error/session state.
 */

/**
 * Cart hook for the RealKevbase storefront.
 *
 * @param {UseCartOptions} [options] Optional overrides.
 * @returns {UseCartResult} Cart state, totals and actions.
 */
export function useCart(options = {}) {
  const {
    api = defaultCheckoutApi,
    navigate = defaultNavigate,
    storageKey = DEFAULT_CART_STORAGE_KEY,
    persist = true,
    storage,
    initialItems,
  } = options

  const [state, dispatch] = useReducer(cartReducer, undefined, () => {
    const items = initialItems
      ? initialItems.map((item) => normalizeCartItem(item))
      : persist
        ? readStoredCart(storage ?? getDefaultStorage(), storageKey)
        : []
    return { ...initialCartState, items }
  })

  const [checkoutState, setCheckoutState] = useState(
    /** @type {{ status: string, error: Error | null, session: import('../api/checkoutApi').CheckoutSession | null }} */ ({
      status: CHECKOUT_STATUS.IDLE,
      error: null,
      session: null,
    }),
  )

  const submittingRef = useRef(false)

  useEffect(() => {
    if (!persist) return
    writeStoredCart(storage ?? getDefaultStorage(), storageKey, state.items)
  }, [persist, state.items, storage, storageKey])

  const totals = useMemo(() => selectTotals(state.items), [state.items])

  const addItem = useCallback((item, quantity = 1) => {
    const normalized = normalizeCartItem({ ...item, quantity })
    dispatch({ type: 'cart/addItem', item: normalized })
  }, [])

  const removeItem = useCallback((id) => {
    dispatch({ type: 'cart/removeItem', id })
  }, [])

  const updateQuantity = useCallback((id, quantity) => {
    dispatch({ type: 'cart/setQuantity', id, quantity })
  }, [])

  const increment = useCallback(
    (id) => {
      const item = state.items.find((candidate) => candidate.id === id)
      if (!item) return
      dispatch({ type: 'cart/setQuantity', id, quantity: item.quantity + 1 })
    },
    [state.items],
  )

  const decrement = useCallback(
    (id) => {
      const item = state.items.find((candidate) => candidate.id === id)
      if (!item) return
      dispatch({ type: 'cart/setQuantity', id, quantity: item.quantity - 1 })
    },
    [state.items],
  )

  const clearCart = useCallback(() => {
    dispatch({ type: 'cart/clear' })
    setCheckoutState({ status: CHECKOUT_STATUS.IDLE, error: null, session: null })
  }, [])

  const resetCheckout = useCallback(() => {
    setCheckoutState({ status: CHECKOUT_STATUS.IDLE, error: null, session: null })
  }, [])

  const checkout = useCallback(
    async ({ signal } = {}) => {
      if (submittingRef.current) return null

      if (state.items.length === 0) {
        const error = new Error('Cannot start checkout: the cart is empty.')
        setCheckoutState({ status: CHECKOUT_STATUS.ERROR, error, session: null })
        return null
      }

      submittingRef.current = true
      setCheckoutState({ status: CHECKOUT_STATUS.SUBMITTING, error: null, session: null })

      try {
        const session = await api.createCheckoutSession(state.items, { signal })

        if (session.url) {
          setCheckoutState({ status: CHECKOUT_STATUS.REDIRECTING, error: null, session })
          navigate(session.url)
        } else {
          // Session created without a redirect URL: let the consumer drive Stripe.js.
          setCheckoutState({ status: CHECKOUT_STATUS.IDLE, error: null, session })
        }

        return session
      } catch (error) {
        const failure = error instanceof Error ? error : new Error('Checkout failed.')
        // Aborting is a deliberate user action, not a checkout error.
        if (failure.name === 'AbortError') {
          setCheckoutState({ status: CHECKOUT_STATUS.IDLE, error: null, session: null })
          return null
        }

        setCheckoutState({ status: CHECKOUT_STATUS.ERROR, error: failure, session: null })
        return null
      } finally {
        submittingRef.current = false
      }
    },
    [api, navigate, state.items],
  )

  return {
    items: state.items,
    totals,
    isEmpty: state.items.length === 0,
    addItem,
    removeItem,
    updateQuantity,
    increment,
    decrement,
    clearCart,
    checkout,
    checkoutStatus: checkoutState.status,
    checkoutError: checkoutState.error,
    checkoutSession: checkoutState.session,
    resetCheckout,
  }
}

export default useCart
