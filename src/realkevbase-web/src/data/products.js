import { FULFILLMENT } from '../cart/cartReducer.js'

/**
 * Demo catalog for the storefront.
 *
 * The backend has no catalog endpoint yet (its placeholder catalog holds the first and
 * fourth item below), so the grid ships with local demo data. Replace this module with a
 * fetch against the product API once it exists; the shape already matches what `useCart`
 * expects (`id`, `name`, `unitPrice`, `fulfillment`, `pdfBlobName`).
 *
 * @typedef {Object} CatalogProduct
 * @property {string} id Matches the backend product id.
 * @property {string} name Display name.
 * @property {string} description Short sales copy.
 * @property {number} unitPrice Price in USD.
 * @property {'DigitalPdf' | 'PhysicalMerch'} fulfillment Delivery type.
 * @property {string | null} pdfBlobName Blob name for PDFs, null for merch.
 * @property {string} badge Short media-overlay label.
 * @property {string} feature Delivery promise shown on the card.
 * @property {{ from: string, to: string, ink: string }} accent Card artwork colors.
 */

/** @type {CatalogProduct[]} */
export const demoProducts = [
  {
    id: '6f1b6a9e-2f4c-4f1e-9a1d-0c2b3e4d5f60',
    name: 'The RealKevbase Field Guide',
    description:
      'A practical, no-fluff guide to taking a product from idea to first paying customer.',
    unitPrice: 19.99,
    fulfillment: FULFILLMENT.DIGITAL_PDF,
    pdfBlobName: 'guides/field-guide.pdf',
    badge: 'Instant PDF',
    feature: 'Download link valid for 24 hours',
    accent: { from: '#E7EEFF', to: '#C7D8FF', ink: '#1D4ED8' },
  },
  {
    id: 'b2f4a1c8-5d6e-4a7b-8c9d-1e2f3a4b5c6d',
    name: 'The Launch Playbook',
    description:
      'Positioning, pricing and launch checklists distilled from real product launches.',
    unitPrice: 29.0,
    fulfillment: FULFILLMENT.DIGITAL_PDF,
    pdfBlobName: 'guides/launch-playbook.pdf',
    badge: 'Instant PDF',
    feature: 'Download link valid for 24 hours',
    accent: { from: '#EAF7F1', to: '#C6EBDD', ink: '#0F766E' },
  },
  {
    id: 'c3a5b2d9-6e7f-4b8c-9d0e-2f3a4b5c6d7e',
    name: 'Systems Notebook',
    description:
      'Printable worksheets for planning a focused build week and keeping momentum.',
    unitPrice: 12.5,
    fulfillment: FULFILLMENT.DIGITAL_PDF,
    pdfBlobName: 'guides/systems-notebook.pdf',
    badge: 'Instant PDF',
    feature: 'Download link valid for 24 hours',
    accent: { from: '#F1ECFF', to: '#DCD0FF', ink: '#6D28D9' },
  },
  {
    id: '9c2d4e6f-8a0b-4c1d-9e3f-5a7b9c1d3e5f',
    name: 'RealKevbase Logo Tee',
    description:
      'Heavyweight organic cotton with a minimal chest print that survives the wash cycle.',
    unitPrice: 24.0,
    fulfillment: FULFILLMENT.PHYSICAL_MERCH,
    pdfBlobName: null,
    badge: 'Ships worldwide',
    feature: 'Printed and shipped in 2–4 days',
    accent: { from: '#F6EFE3', to: '#E8D8BF', ink: '#92400E' },
  },
  {
    id: 'd4b6c3e1-7f80-4c9d-8e1f-3a4b5c6d7e8f',
    name: 'Heavyweight Hoodie',
    description:
      'Brushed fleece, boxy fit and an embroidered mark. Built for long build sessions.',
    unitPrice: 59.0,
    fulfillment: FULFILLMENT.PHYSICAL_MERCH,
    pdfBlobName: null,
    badge: 'Ships worldwide',
    feature: 'Printed and shipped in 2–4 days',
    accent: { from: '#ECEFF3', to: '#D4DAE2', ink: '#334155' },
  },
  {
    id: 'e5c7d4f2-8091-4d0e-9f2a-4b5c6d7e8f90',
    name: 'Studio Desk Mat',
    description:
      'Stitched-edge felt mat sized for a keyboard, notebook and a mug that never moves.',
    unitPrice: 34.0,
    fulfillment: FULFILLMENT.PHYSICAL_MERCH,
    pdfBlobName: null,
    badge: 'Ships worldwide',
    feature: 'Printed and shipped in 2–4 days',
    accent: { from: '#EEF0E4', to: '#D8DEC4', ink: '#4D7C0F' },
  },
]

export default demoProducts
