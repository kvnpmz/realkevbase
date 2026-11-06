import { useEffect, useMemo, useRef, useState } from 'react'

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
} from '@mui/material'
import AddRoundedIcon from '@mui/icons-material/AddRounded'
import BoltRoundedIcon from '@mui/icons-material/BoltRounded'
import CheckRoundedIcon from '@mui/icons-material/CheckRounded'
import CheckroomRoundedIcon from '@mui/icons-material/CheckroomRounded'
import LocalShippingOutlinedIcon from '@mui/icons-material/LocalShippingOutlined'
import MenuBookRoundedIcon from '@mui/icons-material/MenuBookRounded'

import { useCartContext } from '../cart/cartContext.js'
import { FULFILLMENT } from '../cart/cartReducer.js'
import { formatMoney } from '../cart/formatMoney.js'

/** Filter values used by the grid's segmented control. */
const FILTERS = [
  { value: 'all', label: 'All' },
  { value: FULFILLMENT.DIGITAL_PDF, label: 'Digital PDFs' },
  { value: FULFILLMENT.PHYSICAL_MERCH, label: 'Merch' },
]

/**
 * A single product card. Digital PDFs and physical merchandise share the layout but
 * differ in badge, artwork and delivery promise.
 *
 * @param {{ product: import('../data/products.js').CatalogProduct }} props Component props.
 * @returns {React.JSX.Element} The card.
 */
function ProductCard({ product }) {
  const { addItem } = useCartContext()
  const [justAdded, setJustAdded] = useState(false)
  const resetTimer = useRef(null)

  useEffect(
    () => () => {
      if (resetTimer.current) clearTimeout(resetTimer.current)
    },
    [],
  )

  const isDigital = product.fulfillment === FULFILLMENT.DIGITAL_PDF
  const { from, to, ink } = product.accent

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
        sx={{
          position: 'relative',
          height: 172,
          display: 'grid',
          placeItems: 'center',
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
            bgcolor: 'rgba(255, 255, 255, 0.86)',
            backdropFilter: 'blur(4px)',
          }}
        />
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
          {product.name}
        </Typography>

        <Typography variant="body2" color="text.secondary" sx={{ flexGrow: 1 }}>
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
            <Typography variant="h6" sx={{ fontWeight: 800 }}>
              {formatMoney(product.unitPrice)}
            </Typography>
            <Typography variant="caption" color="text.secondary">
              {isDigital ? 'instant download' : 'plus shipping'}
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
 * Responsive product section showing digital PDFs and physical merchandise in one grid,
 * with a segmented filter for each fulfillment type.
 *
 * @param {{ products?: import('../data/products.js').CatalogProduct[] }} props Component props.
 * @returns {React.JSX.Element} The product section.
 */
export function ProductGrid({ products = [] }) {
  const [filter, setFilter] = useState('all')

  const visibleProducts = useMemo(
    () => (filter === 'all' ? products : products.filter((product) => product.fulfillment === filter)),
    [filter, products],
  )

  const handleFilterChange = (_event, nextFilter) => {
    // Keep the current filter when the active toggle is clicked again.
    if (nextFilter !== null) setFilter(nextFilter)
  }

  return (
    <Box
      component="section"
      id="products"
      sx={{ py: { xs: 7, md: 10 }, scrollMarginTop: { xs: '72px', md: '88px' } }}
    >
      <Container maxWidth="lg">
        <Stack
          direction={{ xs: 'column', md: 'row' }}
          spacing={2}
          sx={{
            mb: { xs: 4, md: 5 },
            alignItems: { xs: 'flex-start', md: 'flex-end' },
            justifyContent: 'space-between',
          }}
        >
          <Stack spacing={1} sx={{ maxWidth: 560 }}>
            <Typography variant="overline" color="secondary.main">
              The catalog
            </Typography>
            <Typography variant="h2">Two ways to buy. One cart.</Typography>
            <Typography variant="body1" color="text.secondary">
              Download a guide the second you pay, or add merch to the same order. Totals
              update instantly across both.
            </Typography>
          </Stack>

          <ToggleButtonGroup
            value={filter}
            exclusive
            onChange={handleFilterChange}
            aria-label="Filter products by fulfillment type"
            size="small"
            sx={{
              alignSelf: { xs: 'stretch', md: 'auto' },
              bgcolor: 'background.paper',
              borderRadius: 999,
              p: 0.5,
              border: '1px solid',
              borderColor: 'divider',
              '& .MuiToggleButton-root': {
                border: 0,
                borderRadius: '999px !important',
                flex: { xs: 1, md: 'initial' },
                px: { xs: 1.25, md: 2 },
                py: 0.75,
                fontSize: { xs: 13, md: 14 },
                fontWeight: 600,
                textTransform: 'none',
                whiteSpace: 'nowrap',
              },
            }}
          >
            {FILTERS.map((option) => (
              <ToggleButton key={option.value} value={option.value} aria-label={option.label}>
                {option.label}
              </ToggleButton>
            ))}
          </ToggleButtonGroup>
        </Stack>

        <Grid container spacing={{ xs: 2, md: 3 }}>
          {visibleProducts.map((product) => (
            <Grid key={product.id} size={{ xs: 12, sm: 6, md: 4 }}>
              <ProductCard product={product} />
            </Grid>
          ))}
        </Grid>

        {visibleProducts.length === 0 && (
          <Typography color="text.secondary" sx={{ py: 6, textAlign: 'center' }}>
            Nothing in this collection yet. Check back soon.
          </Typography>
        )}
      </Container>
    </Box>
  )
}

export default ProductGrid
