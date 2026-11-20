import { useCallback, useEffect, useState } from 'react'

import { catalogApi } from '../api/catalogApi'
import type { CatalogProduct } from '../api/catalogApi'

export type { CatalogProduct }

/** Catalog state while a request is in flight, settled or failed. */
interface CatalogState {
  products: CatalogProduct[]
  loading: boolean
  error: Error | null
}

/** What `useCatalog` returns. */
export interface UseCatalogResult {
  products: CatalogProduct[]
  loading: boolean
  error: Error | null
  reload: () => void
}

/**
 * Loads the storefront catalog from the API.
 *
 * The grid renders nothing until the request settles, so a slow or failing API can never show
 * stale product data. `reload` re-runs the request, which is what the error state's retry uses.
 *
 * @returns Catalog state and a retry function.
 */
export function useCatalog(): UseCatalogResult {
  const [state, setState] = useState<CatalogState>({
    products: [],
    loading: true,
    error: null,
  })
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    const controller = new AbortController()

    catalogApi
      .getCatalog({ signal: controller.signal })
      .then(({ products }) => {
        setState({ products, loading: false, error: null })
      })
      .catch((error: unknown) => {
        if (error instanceof Error && error.name === 'AbortError') return
        setState({
          products: [],
          loading: false,
          error: error instanceof Error ? error : new Error('The catalog could not be loaded.'),
        })
      })

    return () => controller.abort()
  }, [reloadToken])

  const reload = useCallback(() => {
    setState({ products: [], loading: true, error: null })
    setReloadToken((token) => token + 1)
  }, [])

  return { ...state, reload }
}
