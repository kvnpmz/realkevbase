import { useEffect, useMemo, useRef, useState } from 'react'

import { Link } from 'react-router-dom'

import {
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Container,
  Divider,
  Grid,
  Stack,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
  useMediaQuery,
} from '@mui/material'
import AddRoundedIcon from '@mui/icons-material/AddRounded'
import BoltRoundedIcon from '@mui/icons-material/BoltRounded'
import CheckRoundedIcon from '@mui/icons-material/CheckRounded'
import CheckroomRoundedIcon from '@mui/icons-material/CheckroomRounded'
import LocalShippingOutlinedIcon from '@mui/icons-material/LocalShippingOutlined'
import MenuBookRoundedIcon from '@mui/icons-material/MenuBookRounded'

import type { CatalogProduct } from '../api/catalogApi'
import { useCartContext } from '../cart/cartContext'
import { FULFILLMENT } from '../cart/cartReducer'
import { formatMoney } from '../cart/formatMoney'
import { CATEGORIES } from '../data/categories'
import type { CategoryFilterValue } from '../data/categories'

/** Cards rendered per page. Phones get a shorter first page so the grid stays scannable. */
const PAGE_SIZE_MOBILE = 6
const PAGE_SIZE_DESKTOP = 12

/**
 * Browse families named in the grid intro, derived from {@link CATEGORIES}.
 *
 * Derived rather than written out so a new family appears in the sentence the moment it is
 * added, instead of leaving copy behind that quietly describes an older, smaller catalogue.
 * `all` and `merch` are filters, not families, so they are left out.
 */
const FAMILY_LABELS = CATEGORIES.filter(
  (option) => option.value !== 'all' && option.value !== 'merch',
).map((option) => option.label)

/**
 * Joins labels the way a sentence does: "A, B and C".
 */
const joinLabels = (labels: readonly string[]): string =>
  labels.length < 2 ? labels.join('') : `${labels.slice(0, -1).join(', ')} and ${labels.at(-1)}`

/**
 * A single product card. Digital PDFs and physical merchandise share the layout but
 * differ in badge, artwork and delivery promise.
 */
function ProductCard({ product }: { product: CatalogProduct }) {
  const { addItem } = useCartContext()
  const [justAdded, setJustAdded] = useState(false)
  const [thumbnailFailed, setThumbnailFailed] = useState(false)
  const resetTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(
    () => () => {
      if (resetTimer.current) clearTimeout(resetTimer.current)
    },
    [],
  )

  const isDigital = product.fulfillment === FULFILLMENT.DIGITAL_PDF
  const { from, to, ink } = product.accent
  // Merch has no detail page, so only a published guide becomes a link.
  const detailPath = isDigital && product.slug ? `/guides/${product.slug}` : null

  const handleAdd = () => {
    addItem(product, 1)
    setJustAdded(true)
    if (resetTimer.current) clearTimeout(resetTimer.current)
    resetTimer.current = setTimeout(() => setJustAdded(false), 1800)
  }

  return (
    <Card
      sx={{
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        transition: 'transform 180ms ease, box-shadow 180ms ease',
        '&:hover': {
          transform: 'translateY(-4px)',
          boxShadow: '0 18px 40px -24px rgba(20, 22, 26, 0.35)',
        },
      }}
    >
      <Box
        component={detailPath ? Link : 'div'}
        {...(detailPath ? { to: detailPath, tabIndex: -1, 'aria-hidden': true } : {})}
        sx={{
          position: 'relative',
          display: 'grid',
          placeItems: 'center',
          height: 172,
          color: 'inherit',
          textDecoration: 'none',
          background: `linear-gradient(135deg, ${from} 0%, ${to} 100%)`,
        }}
      >
        <Chip
          label={product.badge}
          size="small"
          sx={{
            position: 'absolute',
            top: 12,
            left: 12,
            zIndex: 2,
            bgcolor: 'rgba(255, 255, 255, 0.86)',
            backdropFilter: 'blur(4px)',
          }}
        />
        {product.thumbnailUrl && !thumbnailFailed ? (
          <Box
            component="img"
            src={product.thumbnailUrl}
            alt={`${product.name} — printable PDF cover`}
            loading="lazy"
            decoding="async"
            onError={() => setThumbnailFailed(true)}
            sx={{
              position: 'absolute',
              inset: 0,
              width: '100%',
              height: '100%',
              objectFit: 'contain',
              p: 1.5,
              filter: 'drop-shadow(0 8px 18px rgba(20, 22, 26, 0.18))',
            }}
          />
        ) : (
          <Box
            aria-hidden="true"
            sx={{
              width: 64,
              height: 64,
              borderRadius: '18px',
              display: 'grid',
              placeItems: 'center',
              bgcolor: 'rgba(255, 255, 255, 0.72)',
              color: ink,
            }}
          >
            {isDigital ? (
              <MenuBookRoundedIcon sx={{ fontSize: 34 }} />
            ) : (
              <CheckroomRoundedIcon sx={{ fontSize: 34 }} />
            )}
          </Box>
        )}
      </Box>

      <CardContent
        sx={{ display: 'flex', flexDirection: 'column', gap: 1.25, flexGrow: 1, p: 2.5 }}
      >
        <Typography
          variant="overline"
          sx={{ color: isDigital ? '#1D4ED8' : '#92400E', lineHeight: 1.4 }}
        >
          {isDigital ? 'Digital PDF' : 'Physical merch'}
        </Typography>

        <Typography variant="h6" component="h3">
          {detailPath ? (
            <Box
              component={Link}
              to={detailPath}
              sx={{
                color: 'inherit',
                textDecoration: 'none',
                '&:hover': { textDecoration: 'underline' },
              }}
            >
              {product.name}
            </Box>
          ) : (
            product.name
          )}
        </Typography>

        {/* Shop copy is longer than card copy, so clamp it and keep the full text on
            the guide page. */}
        <Typography
          variant="body2"
          color="text.secondary"
          component="p"
          sx={{
            flexGrow: 1,
            display: '-webkit-box',
            WebkitBoxOrient: 'vertical',
            WebkitLineClamp: 5,
            overflow: 'hidden',
          }}
        >
          {product.description}
        </Typography>

        <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center', color: 'text.secondary' }}>
          {isDigital ? (
            <BoltRoundedIcon sx={{ fontSize: 16 }} />
          ) : (
            <LocalShippingOutlinedIcon sx={{ fontSize: 16 }} />
          )}
          <Typography variant="caption">{product.feature}</Typography>
        </Stack>

        <Divider sx={{ my: 0.5 }} />

        <Stack
          direction="row"
          spacing={1.5}
          sx={{
            alignItems: 'center',
            justifyContent: 'space-between',
            flexWrap: 'wrap',
            rowGap: 1.5,
          }}
        >
          <Box>
            <Typography component="p" variant="h6" sx={{ fontWeight: 800 }}>
              {formatMoney(product.unitPrice)}
            </Typography>
            {/* A label under a price, so it starts a sentence and takes a capital; the
                delivery promises further down the card are prose and stay lowercase. */}
            <Typography variant="caption" color="text.secondary">
              {isDigital ? 'Instant download' : 'Plus shipping'}
            </Typography>
          </Box>

          <Button
            onClick={handleAdd}
            variant="contained"
            color={justAdded ? 'success' : 'primary'}
            startIcon={justAdded ? <CheckRoundedIcon /> : <AddRoundedIcon />}
            aria-live="polite"
            sx={{ whiteSpace: 'nowrap' }}
          >
            {justAdded ? 'Added' : 'Add to cart'}
          </Button>
        </Stack>
      </CardContent>
    </Card>
  )
}

/**
 * Responsive product section showing the whole catalog in browseable categories, with a
 * page of cards at a time and a `Show more` control for the rest.
 */
export interface ProductGridProps {
  /** Products to render. */
  products?: CatalogProduct[]
  /** Whether the catalog request is still in flight. */
  loading?: boolean
  /** Catalog load failure, when there is one. */
  error?: Error | null
  /** Retries the catalog request. */
  onRetry?: () => void
}

export function ProductGrid({ products = [], loading = false, error = null, onRetry }: ProductGridProps) {
  const isDesktop = useMediaQuery('(min-width:600px)')
  const pageSize = isDesktop ? PAGE_SIZE_DESKTOP : PAGE_SIZE_MOBILE

  const [category, setCategory] = useState<CategoryFilterValue>('all')
  const [extraPages, setExtraPages] = useState(0)

  // Kept in sync with the catalog instead of a hard-coded count, so adding a listing
  // never leaves the intro promising the wrong number of PDFs.
  const pdfCount = useMemo(
    () => products.filter((product) => product.fulfillment === FULFILLMENT.DIGITAL_PDF).length,
    [products],
  )

  const filteredProducts = useMemo(() => {
    if (category === 'all') return products
    if (category === 'merch') {
      return products.filter((product) => product.fulfillment === FULFILLMENT.PHYSICAL_MERCH)
    }
    return products.filter((product) => product.category === category)
  }, [category, products])

  // Keeping extra pages rather than a raw count lets the page size follow the breakpoint.
  const visibleProducts = filteredProducts.slice(0, pageSize * (extraPages + 1))
  const hasMore = filteredProducts.length > visibleProducts.length

  const handleCategoryChange = (_event: React.SyntheticEvent, nextCategory: CategoryFilterValue | null) => {
    // Keep the current category when the active toggle is clicked again.
    if (nextCategory === null) return
    setCategory(nextCategory)
    setExtraPages(0)
  }

  return (
    <Box
      component="section"
      id="products"
      // The hero above already ends on its own padding, and stacked the two left
      // ~120px of empty page between the chips and this headline.
      sx={{ pt: { xs: 4, md: 5 }, pb: { xs: 7, md: 10 }, scrollMarginTop: { xs: '72px', md: '88px' } }}
    >
      <Container maxWidth="lg">
        <Stack spacing={{ xs: 2.5, md: 3 }} sx={{ mb: { xs: 4, md: 5 } }}>
          <Stack spacing={1} sx={{ maxWidth: 560 }}>
            <Typography variant="overline" color="secondary.main">
              Browse the collection
            </Typography>
            <Typography variant="h2">Find your next checklist.</Typography>
            <Typography variant="body1" color="text.secondary">
              {pdfCount > 0 ? `${pdfCount} PDFs` : 'Printable PDFs'} across{' '}
              {joinLabels(FAMILY_LABELS)}. Delivered the moment you pay.
            </Typography>
          </Stack>

          <Box
            sx={{
              overflowX: 'auto',
              mx: { xs: -2, md: 0 },
              px: { xs: 2, md: 0 },
              pb: 0.5,
              scrollbarWidth: 'none',
              '&::-webkit-scrollbar': { display: 'none' },
            }}
          >
            <ToggleButtonGroup
              value={category}
              exclusive
              onChange={handleCategoryChange}
              aria-label="Filter products by category"
              size="small"
              sx={{
                width: { xs: 'max-content', md: 'auto' },
                bgcolor: 'background.paper',
                borderRadius: 999,
                p: 0.5,
                border: '1px solid',
                borderColor: 'divider',
                '& .MuiToggleButton-root': {
                  border: 0,
                  borderRadius: '999px !important',
                  px: { xs: 1.5, md: 2 },
                  py: 0.75,
                  fontSize: { xs: 13, md: 14 },
                  fontWeight: 600,
                  textTransform: 'none',
                  whiteSpace: 'nowrap',
                },
              }}
            >
              {CATEGORIES.map((option) => (
                <ToggleButton key={option.value} value={option.value} aria-label={option.label}>
                  {option.label}
                </ToggleButton>
              ))}
            </ToggleButtonGroup>
          </Box>
        </Stack>

        {loading && (
          <Typography color="text.secondary" sx={{ py: 6, textAlign: 'center' }}>
            Loading the collection…
          </Typography>
        )}

        {!loading && error && (
          <Stack spacing={2} sx={{ py: 6, alignItems: 'center' }}>
            <Typography color="text.secondary" sx={{ textAlign: 'center', maxWidth: 420 }}>
              We couldn’t load the collection. Check your connection and try again.
            </Typography>
            {onRetry && (
              <Button variant="outlined" onClick={onRetry}>
                Try again
              </Button>
            )}
          </Stack>
        )}

        {!loading && !error && (
          <>
            <Grid container spacing={{ xs: 2, md: 3 }}>
              {visibleProducts.map((product) => (
                <Grid key={product.id} size={{ xs: 12, sm: 6, md: 4 }}>
                  <ProductCard product={product} />
                </Grid>
              ))}
            </Grid>

            {filteredProducts.length === 0 && (
              <Typography color="text.secondary" sx={{ py: 6, textAlign: 'center' }}>
                Nothing in this category yet. Check back soon.
              </Typography>
            )}

            {filteredProducts.length > 0 && (
              <Stack spacing={1.5} sx={{ alignItems: 'center', mt: { xs: 4, md: 5 } }}>
                {hasMore && (
                  <Button variant="outlined" onClick={() => setExtraPages((pages) => pages + 1)}>
                    Show more
                  </Button>
                )}
                <Typography variant="caption" color="text.secondary" aria-live="polite">
                  Showing {visibleProducts.length} of {filteredProducts.length}
                </Typography>
              </Stack>
            )}
          </>
        )}
      </Container>
    </Box>
  )
}

export default ProductGrid
