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

import { checkoutApi as defaultCheckoutApi } from '../api/checkoutApi'
import type { CheckoutSession } from '../api/checkoutApi'
import { trackBeginCheckout } from '../analytics/ga'
import { cartReducer, initialCartState, normalizeCartItem, selectTotals } from '../cart/cartReducer'
import type { CartItem, CartState } from '../cart/cartReducer'
import { DEFAULT_CART_STORAGE_KEY, getDefaultStorage, readStoredCart, writeStoredCart } from '../cart/cartStorage'

/**
 * Lifecycle of a checkout attempt.
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

/** One of the values in CHECKOUT_STATUS. */
export type CheckoutStatus = (typeof CHECKOUT_STATUS)[keyof typeof CHECKOUT_STATUS]

/** The state machine behind `checkoutStatus`, `checkoutError` and `checkoutSession`. */
interface CheckoutState {
  status: CheckoutStatus
  error: Error | null
  session: CheckoutSession | null
}

/** A cart line before it is given a quantity. */
export type CartItemInput = Omit<CartItem, 'quantity'>

/** Options accepted by `useCart`, mainly overrides for tests. */
export interface UseCartOptions {
  /** Checkout client override (defaults to the shared client). */
  api?: typeof defaultCheckoutApi
  /** Redirect function override (defaults to window.location.assign). */
  navigate?: (url: string) => void
  /** localStorage key for the cart. */
  storageKey?: string
  /** Set false to disable persistence. */
  persist?: boolean
  /** Storage override, mainly for tests. */
  storage?: Storage | null
  /** Seed items instead of reading storage. */
  initialItems?: CartItem[]
}

/** Everything `useCart` returns. */
export interface UseCartResult {
  /** Current cart lines. */
  items: CartItem[]
  /** Aggregated totals. */
  totals: ReturnType<typeof selectTotals>
  /** Whether the cart holds no lines. */
  isEmpty: boolean
  addItem: (item: CartItemInput, quantity?: number) => void
  removeItem: (id: string) => void
  updateQuantity: (id: string, quantity: number) => void
  increment: (id: string) => void
  decrement: (id: string) => void
  clearCart: () => void
  checkout: (options?: { signal?: AbortSignal }) => Promise<CheckoutSession | null>
  /** One of CHECKOUT_STATUS. */
  checkoutStatus: CheckoutStatus
  /** Last checkout failure (a CheckoutApiError when the API responded). */
  checkoutError: Error | null
  /** Most recent session. */
  checkoutSession: CheckoutSession | null
  /** Clear checkout error/session state. */
  resetCheckout: () => void
}

/**
 * Default redirect implementation: leave the SPA for Stripe's hosted page.
 * @param url Stripe Checkout URL.
 */
function defaultNavigate(url: string): void {
  window.location.assign(url)
}

/**
 * Cart hook for the RealKevbase storefront.
 *
 * @param options Optional overrides.
 * @returns Cart state, totals and actions.
 */
export function useCart(options: UseCartOptions = {}): UseCartResult {
  const {
    api = defaultCheckoutApi,
    navigate = defaultNavigate,
    storageKey = DEFAULT_CART_STORAGE_KEY,
    persist = true,
    storage,
    initialItems,
  } = options

  const [state, dispatch] = useReducer(cartReducer, undefined, (): CartState => {
    const items = initialItems
      ? initialItems.map((item) => normalizeCartItem(item))
      : persist
        ? readStoredCart(storage ?? getDefaultStorage(), storageKey)
        : []
    return { ...initialCartState, items }
  })

  const [checkoutState, setCheckoutState] = useState<CheckoutState>({
    status: CHECKOUT_STATUS.IDLE,
    error: null,
    session: null,
  })

  const submittingRef = useRef(false)

  useEffect(() => {
    if (!persist) return
    writeStoredCart(storage ?? getDefaultStorage(), storageKey, state.items)
  }, [persist, state.items, storage, storageKey])

  const totals = useMemo(() => selectTotals(state.items), [state.items])

  const addItem = useCallback((item: CartItemInput, quantity = 1) => {
    const normalized = normalizeCartItem({ ...item, quantity })
    dispatch({ type: 'cart/addItem', item: normalized })
  }, [])

  const removeItem = useCallback((id: string) => {
    dispatch({ type: 'cart/removeItem', id })
  }, [])

  const updateQuantity = useCallback((id: string, quantity: number) => {
    dispatch({ type: 'cart/setQuantity', id, quantity })
  }, [])

  const increment = useCallback(
    (id: string) => {
      const item = state.items.find((candidate) => candidate.id === id)
      if (!item) return
      dispatch({ type: 'cart/setQuantity', id, quantity: item.quantity + 1 })
    },
    [state.items],
  )

  const decrement = useCallback(
    (id: string) => {
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
    async ({ signal }: { signal?: AbortSignal } = {}): Promise<CheckoutSession | null> => {
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

        // Funnel step 2 (after add_to_cart): records value and items, and stashes them so
        // the post-redirect success page can emit a purchase event with the same value.
        trackBeginCheckout(state.items, totals)

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
    [api, navigate, state.items, totals],
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
