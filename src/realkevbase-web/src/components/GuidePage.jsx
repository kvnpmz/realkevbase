import { useEffect, useMemo, useRef, useState } from 'react'

import { Link, useParams } from 'react-router-dom'

import { Box, Button, Chip, Container, Divider, Grid, IconButton, Stack, Typography } from '@mui/material'
import AddRoundedIcon from '@mui/icons-material/AddRounded'
import ArrowBackRoundedIcon from '@mui/icons-material/ArrowBackRounded'
import BoltRoundedIcon from '@mui/icons-material/BoltRounded'
import CheckRoundedIcon from '@mui/icons-material/CheckRounded'
import MenuBookRoundedIcon from '@mui/icons-material/MenuBookRounded'
import PauseRoundedIcon from '@mui/icons-material/PauseRounded'
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded'

import { trackEvent } from '../analytics/ga.js'
import { readInlineProduct } from '../api/catalogApi.js'
import { useCartContext } from '../cart/cartContext.js'
import { FULFILLMENT } from '../cart/cartReducer.js'
import { formatMoney } from '../cart/formatMoney.js'
import { CATEGORIES } from '../data/categories.js'
import { SITE_NAME, SITE_TITLE } from '../data/site.js'

/** Category value to its storefront label. */
const CATEGORY_LABELS = new Map(CATEGORIES.map((option) => [option.value, option.label]))

/**
 * Promises shown under the price, all of them true of the product being viewed.
 *
 * @param {readonly string[]} pageSizes Paper sizes the API says the listing is sold at.
 * @returns {{ icon: typeof BoltRoundedIcon, copy: string }[]} Promise rows.
 */
const guidePromises = (pageSizes) => [
  { icon: BoltRoundedIcon, copy: 'Instant download the moment your payment clears' },
  { icon: CheckRoundedIcon, copy: `Prints on ${pageSizes.join(' and ')} paper` },
  { icon: CheckRoundedIcon, copy: 'Secure link stays live for 24 hours' },
]

/**
 * Centred message used by the loading, error and not-found states.
 *
 * @param {{ title: string, copy?: string, action?: React.ReactNode }} props Component props.
 * @returns {React.JSX.Element} The message block.
 */
function PageMessage({ title, copy, action }) {
  return (
    // Full height on purpose: this is what a guide shows while the catalog is still in
    // flight, and a short message left the site footer sitting in the first screen, so the
    // footer dropped hundreds of pixels when the guide replaced it — 0.08 of layout shift
    // on every deep link. Filling the viewport keeps the footer off-screen until the real
    // page is there to hold it down.
    <Container maxWidth="sm" sx={{ py: { xs: 10, md: 16 }, minHeight: '100vh' }}>
      <Stack spacing={2} sx={{ alignItems: 'center', textAlign: 'center' }}>
        <Typography variant="h4" component="h1">
          {title}
        </Typography>
        {copy && (
          <Typography color="text.secondary" sx={{ maxWidth: 420 }}>
            {copy}
          </Typography>
        )}
        {action}
      </Stack>
    </Container>
  )
}

/**
 * Gallery and buy box for one digital guide.
 *
 * Kept separate from the route so its state resets when the visitor moves from one guide
 * to another without the component unmounting.
 *
 * @param {{ product: import('../hooks/useCatalog.js').CatalogProduct }} props Component props.
 * @returns {React.JSX.Element} The detail layout.
 */
function GuideDetail({ product }) {
  const { addItem } = useCartContext()
  const [activeSlot, setActiveSlot] = useState(0)
  const [failedSlots, setFailedSlots] = useState(() => new Set())
  const [videoFailed, setVideoFailed] = useState(false)
  const [justAdded, setJustAdded] = useState(false)
  const [videoPlaying, setVideoPlaying] = useState(false)
  const [videoArmed, setVideoArmed] = useState(false)
  const addedTimer = useRef(null)
  const videoRef = useRef(null)

  // The API states the paper sizes the listing is laid out for; fall back to the Letter
  // edition, which every listing has.
  const pageSizes = product.pageSizes ?? ['US Letter']
  const slots = product.galleryUrls ?? []
  const availableSlots = slots.map((_, slot) => slot).filter((slot) => !failedSlots.has(slot))
  const currentSlot = availableSlots.includes(activeSlot) ? activeSlot : (availableSlots[0] ?? null)
  const currentUrl = currentSlot === null ? null : slots[currentSlot]
  const { from, to, ink } = product.accent
  const categoryLabel = CATEGORY_LABELS.get(product.category) ?? 'Digital PDF'

  // The clip is the page itself, so it only stands in for slot 0. The other slots are the
  // zoom, tablet and A4 renders, which have no clip of their own.
  const showVideo = Boolean(product.videoUrl) && currentSlot === 0 && !videoFailed

  const mediaSx = {
    // Absolute positioning is what makes the percentage height resolve; as a grid item the
    // image would keep its intrinsic height and get clipped.
    position: 'absolute',
    inset: 0,
    width: '100%',
    height: '100%',
    objectFit: 'contain',
    p: { xs: 2, md: 4 },
    filter: 'drop-shadow(0 12px 28px rgba(20, 22, 26, 0.2))',
  }

  // Nothing plays on its own. The clip is 546 KB — the single largest transfer on the page,
  // and the reason a 16 KB poster took three seconds to arrive on a throttled phone — so it
  // is not attached to the element until the reader presses play, which keeps it off the
  // critical path entirely. The observer below only ever pauses: a clip whose owner has
  // scrolled away should stop costing battery, but scrolling back must not restart it.
  //
  // Reduced motion needs no branch any more: a visitor who asked for it simply never
  // presses play, and everyone else opts in by choice.
  useEffect(() => {
    const node = videoRef.current
    if (!node || !showVideo) return undefined

    const observer = new IntersectionObserver(
      ([entry]) => {
        if (!entry.isIntersecting) node.pause()
      },
      { threshold: 0.4 },
    )
    observer.observe(node)
    return () => observer.disconnect()
  }, [showVideo])

  const markFailed = (slot) => {
    setFailedSlots((previous) => new Set(previous).add(slot))
  }

  // The first press is what gives the element a source — React assigns `src`, the element
  // is muted and inline, and `autoPlay` starts it as the bytes arrive. Later presses are an
  // ordinary play/pause on a clip that is already downloaded.
  const toggleVideo = () => {
    const node = videoRef.current
    if (!node) return

    if (!videoArmed) {
      setVideoArmed(true)
      return
    }

    if (node.paused) {
      node.muted = true
      node.play().catch(() => {})
    } else {
      node.pause()
    }
  }

  useEffect(() => {
    document.title = `${product.name} — ${SITE_NAME}`
    trackEvent('view_item', {
      currency: 'USD',
      value: product.unitPrice,
      items: [
        { item_id: product.id, item_name: product.name, price: product.unitPrice, quantity: 1 },
      ],
    })

    return () => {
      document.title = SITE_TITLE
    }
  }, [product])

  useEffect(
    () => () => {
      if (addedTimer.current) clearTimeout(addedTimer.current)
    },
    [],
  )

  const handleAdd = () => {
    addItem(product, 1)
    setJustAdded(true)
    if (addedTimer.current) clearTimeout(addedTimer.current)
    addedTimer.current = setTimeout(() => setJustAdded(false), 1800)
  }

  return (
    <Container maxWidth="lg" sx={{ py: { xs: 4, md: 6 } }}>
      <Button
        component={Link}
        to="/#products"
        startIcon={<ArrowBackRoundedIcon />}
        color="inherit"
        sx={{ mb: { xs: 2, md: 3 }, color: 'text.secondary', px: 0 }}
      >
        All guides
      </Button>

      <Grid container spacing={{ xs: 3, md: 5 }}>
        <Grid size={{ xs: 12, md: 7 }}>
          <Box
            sx={{
              position: 'relative',
              height: { xs: 360, sm: 460, md: 540 },
              display: 'grid',
              placeItems: 'center',
              borderRadius: 3,
              overflow: 'hidden',
              background: `linear-gradient(135deg, ${from} 0%, ${to} 100%)`,
            }}
          >
            {showVideo ? (
              <Box
                component="video"
                ref={videoRef}
                src={videoArmed ? product.videoUrl : undefined}
                poster={slots[0]}
                muted
                loop
                playsInline
                autoPlay={videoArmed}
                preload="none"
                aria-label={`${product.name}, silent five second preview`}
                onError={() => setVideoFailed(true)}
                onPlay={() => setVideoPlaying(true)}
                onPause={() => setVideoPlaying(false)}
                sx={mediaSx}
              />
            ) : currentUrl ? (
              <Box
                component="img"
                src={currentUrl}
                alt={`${product.name}, preview ${currentSlot + 1} of ${availableSlots.length}`}
                fetchPriority="high"
                decoding="async"
                onError={() => markFailed(currentSlot)}
                sx={mediaSx}
              />
            ) : (
              <Box
                aria-hidden="true"
                sx={{
                  width: 120,
                  height: 120,
                  borderRadius: 4,
                  display: 'grid',
                  placeItems: 'center',
                  bgcolor: 'rgba(255, 255, 255, 0.72)',
                  color: ink,
                }}
              >
                <MenuBookRoundedIcon sx={{ fontSize: 64 }} />
              </Box>
            )}

            {showVideo && (
              <IconButton
                type="button"
                onClick={toggleVideo}
                aria-label={videoPlaying ? 'Pause the preview' : 'Play the preview'}
                size="small"
                sx={{
                  position: 'absolute',
                  right: { xs: 10, md: 16 },
                  bottom: { xs: 10, md: 16 },
                  bgcolor: 'rgba(20, 22, 26, 0.55)',
                  color: '#fff',
                  backdropFilter: 'blur(4px)',
                  '&:hover': { bgcolor: 'rgba(20, 22, 26, 0.78)' },
                }}
              >
                {videoPlaying ? (
                  <PauseRoundedIcon fontSize="small" />
                ) : (
                  <PlayArrowRoundedIcon fontSize="small" />
                )}
              </IconButton>
            )}
          </Box>

          {availableSlots.length > 1 && (
            <Stack direction="row" spacing={1.25} sx={{ mt: 2, flexWrap: 'wrap' }}>
              {availableSlots.map((slot, index) => (
                <Box
                  key={slot}
                  component="button"
                  type="button"
                  onClick={() => setActiveSlot(slot)}
                  aria-label={`Show preview ${index + 1} of ${availableSlots.length}`}
                  aria-pressed={slot === currentSlot}
                  sx={{
                    position: 'relative',
                    p: 0,
                    width: 76,
                    height: 76,
                    overflow: 'hidden',
                    cursor: 'pointer',
                    borderRadius: 2,
                    bgcolor: 'background.paper',
                    border: '2px solid',
                    borderColor: slot === currentSlot ? 'text.primary' : 'divider',
                  }}
                >
                  <Box
                    component="img"
                    src={slots[slot]}
                    alt=""
                    loading="lazy"
                    onError={() => markFailed(slot)}
                    sx={{
                      position: 'absolute',
                      inset: 0,
                      width: '100%',
                      height: '100%',
                      objectFit: 'cover',
                    }}
                  />
                </Box>
              ))}
            </Stack>
          )}
        </Grid>

        <Grid size={{ xs: 12, md: 5 }}>
          <Stack spacing={2.5}>
            <Stack spacing={1}>
              <Typography variant="overline" sx={{ color: '#1D4ED8' }}>
                {categoryLabel}
              </Typography>
              <Typography variant="h2" component="h1" sx={{ fontSize: { xs: '2rem', md: '2.6rem' } }}>
                {product.name}
              </Typography>
              <Typography variant="body1" color="text.secondary">
                {product.description}
              </Typography>
            </Stack>

            <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', rowGap: 1 }}>
              <Chip label="Digital PDF" size="small" />
              <Chip label={pageSizes.join(' + ')} size="small" variant="outlined" />
              <Chip label="Instant download" size="small" variant="outlined" />
            </Stack>

            <Divider />

            <Stack spacing={1.25}>
              <Stack direction="row" spacing={1.5} sx={{ alignItems: 'baseline' }}>
                <Typography variant="h4" component="p" sx={{ fontWeight: 800 }}>
                  {formatMoney(product.unitPrice)}
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  instant download
                </Typography>
              </Stack>

              {guidePromises(pageSizes).map(({ icon: Icon, copy }) => (
                <Stack key={copy} direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                  <Icon sx={{ fontSize: 18, color: 'text.secondary' }} />
                  <Typography variant="body2" color="text.secondary">
                    {copy}
                  </Typography>
                </Stack>
              ))}
            </Stack>

            <Button
              onClick={handleAdd}
              variant="contained"
              size="large"
              color={justAdded ? 'success' : 'primary'}
              startIcon={justAdded ? <CheckRoundedIcon /> : <AddRoundedIcon />}
              aria-live="polite"
              sx={{ py: 1.5 }}
            >
              {justAdded ? 'Added to cart' : 'Add to cart'}
            </Button>

            <Typography variant="caption" color="text.secondary">
              Checkout is handled by Stripe. Your download link appears the moment payment
              succeeds — no account needed.
            </Typography>
          </Stack>
        </Grid>
      </Grid>
    </Container>
  )
}

/**
 * Product detail route. Resolves the guide from the catalog already loaded by the shell,
 * so a shared link renders without a second catalog request.
 *
 * @param {Object} props Component props.
 * @param {{
 *   products: import('../hooks/useCatalog.js').CatalogProduct[],
 *   loading: boolean,
 *   error: Error | null,
 *   reload: () => void,
 * }} props.catalog Catalog state from `useCatalog`.
 * @returns {React.JSX.Element} The guide page.
 */
export function GuidePage({ catalog }) {
  const { slug } = useParams()
  const { products, loading, error, reload } = catalog

  // The build bakes this guide's own catalog record into the page, so the hero can paint
  // from the HTML instead of waiting for /api/products — the slowest request in the chain.
  // The fetched catalog still wins once it lands, so a price edited between deploys settles
  // in the same visit; the inline copy is only the first paint.
  const inlineProduct = useMemo(() => readInlineProduct(slug), [slug])

  const product = useMemo(
    () =>
      products.find(
        (entry) =>
          entry.fulfillment === FULFILLMENT.DIGITAL_PDF &&
          (entry.slug === slug || entry.id === slug),
      ) ?? inlineProduct,
    [products, slug, inlineProduct],
  )

  if (loading && !product) {
    return <PageMessage title="Loading this guide…" />
  }

  if (error && !product) {
    return (
      <PageMessage
        title="We couldn’t load this guide."
        copy="Check your connection and try again."
        action={
          <Button variant="outlined" onClick={reload}>
            Try again
          </Button>
        }
      />
    )
  }

  if (!product) {
    return (
      <PageMessage
        title="Guide not found"
        copy="This guide may have been renamed or retired. Browse the collection to find it."
        action={
          <Button component={Link} to="/#products" variant="contained" startIcon={<ArrowBackRoundedIcon />}>
            Back to the collection
          </Button>
        }
      />
    )
  }

  return <GuideDetail key={product.id} product={product} />
}

export default GuidePage
