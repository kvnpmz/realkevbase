import { useCallback, useEffect, useState } from 'react'

import { catalogApi } from '../api/catalogApi.js'

/**
 * A product exactly as the card grid renders it: catalog data from the API plus the
 * presentation values the grid needs.
 *
 * @typedef {Object} CatalogProduct
 * @property {string} id Product identifier sent to checkout.
 * @property {string} name Display name.
 * @property {string} description Short sales copy.
 * @property {number} unitPrice Price in USD.
 * @property {'DigitalPdf' | 'PhysicalMerch'} fulfillment Delivery type.
 * @property {string | null} category Browse category, null for merch.
 * @property {string | null} listingId Listing that backs the PDF, null for merch.
 * @property {CatalogListing | null} listing Listing metadata, when the product has one.
 * @property {string | null} thumbnailUrl Absolute URL of the product artwork, when published.
 * @property {string} badge Short media-overlay label.
 * @property {string} feature Delivery promise shown on the card.
 * @property {{ from: string, to: string, ink: string }} accent Card artwork colors.
 */

/**
 * A listing row from the generator database, as exposed by `GET /api/listings`.
 *
 * @typedef {Object} CatalogListing
 * @property {string} id Listing identifier.
 * @property {string | null} outputName Configured output file name.
 * @property {string | null} template Generator template.
 * @property {string | null} completedDate Date the PDF was completed.
 * @property {boolean} isEditable Whether the PDF has an editable text layer.
 */

/**
 * Loads the storefront catalog from the API.
 *
 * The grid renders nothing until the request settles, so a slow or failing API can never show
 * stale product data. `reload` re-runs the request, which is what the error state's retry uses.
 *
 * @returns {{
 *   products: CatalogProduct[],
 *   listings: CatalogListing[],
 *   loading: boolean,
 *   error: Error | null,
 *   reload: () => void,
 * }} Catalog state and a retry function.
 */
export function useCatalog() {
  const [state, setState] = useState({
    products: /** @type {CatalogProduct[]} */ ([]),
    listings: /** @type {CatalogListing[]} */ ([]),
    loading: true,
    error: /** @type {Error | null} */ (null),
  })
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    const controller = new AbortController()

    catalogApi
      .getCatalog({ signal: controller.signal })
      .then(({ products, listings }) => {
        setState({ products, listings, loading: false, error: null })
      })
      .catch((error) => {
        if (error instanceof Error && error.name === 'AbortError') return
        setState({
          products: [],
          listings: [],
          loading: false,
          error: error instanceof Error ? error : new Error('The catalog could not be loaded.'),
        })
      })

    return () => controller.abort()
  }, [reloadToken])

  const reload = useCallback(() => {
    setState({ products: [], listings: [], loading: true, error: null })
    setReloadToken((token) => token + 1)
  }, [])

  return { ...state, reload }
}
