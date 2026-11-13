import { useEffect } from 'react'

import { Box, Button, Container, Grid, Stack, Typography } from '@mui/material'
import ArrowForwardRoundedIcon from '@mui/icons-material/ArrowForwardRounded'
import BoltRoundedIcon from '@mui/icons-material/BoltRounded'
import LocalShippingOutlinedIcon from '@mui/icons-material/LocalShippingOutlined'
import LockOutlinedIcon from '@mui/icons-material/LockOutlined'

import { ProductGrid } from './ProductGrid.jsx'
import { SITE_TITLE } from '../data/site.js'

/** Trust/reassurance points shown below the fold. */
const VALUE_PROPS = [
  {
    icon: BoltRoundedIcon,
    title: 'Instant PDF delivery',
    copy: 'Every guide comes with a secure, pre-signed download link that stays live for 24 hours.',
  },
  {
    icon: LockOutlinedIcon,
    title: 'Stripe-secured checkout',
    copy: 'Cards and wallets are handled by Stripe. Your payment details never touch our servers.',
  },
  {
    icon: LocalShippingOutlinedIcon,
    title: 'Merch, made to last',
    copy: 'Physical orders are printed and shipped within 2–4 business days, worldwide.',
  },
]

/**
 * Landing page: hero, the browseable catalog and the reassurance section.
 *
 * @param {Object} props Component props.
 * @param {import('../hooks/useCatalog.js').CatalogProduct[]} [props.products] Products to render.
 * @param {boolean} [props.loading] Whether the catalog request is still in flight.
 * @param {Error | null} [props.error] Catalog load failure, when there is one.
 * @param {() => void} [props.onRetry] Retries the catalog request.
 * @returns {React.JSX.Element} The landing page.
 */
export function HomePage({ products = [], loading = false, error = null, onRetry }) {
  // The guide page rewrites the tab title; coming back here must restore the storefront's.
  useEffect(() => {
    document.title = SITE_TITLE
  }, [])

  return (
    <>
      <Box sx={{ position: 'relative', overflow: 'hidden' }}>
        <Box
          aria-hidden="true"
          sx={{
            position: 'absolute',
            inset: 0,
            background:
              'radial-gradient(60% 60% at 72% 0%, rgba(47, 107, 255, 0.14), transparent 62%), radial-gradient(45% 45% at 8% 18%, rgba(124, 58, 237, 0.12), transparent 60%)',
          }}
        />
        <Container maxWidth="lg" sx={{ position: 'relative', pt: { xs: 8, md: 12 }, pb: { xs: 4, md: 5 } }}>
          <Stack spacing={3} sx={{ maxWidth: 720 }}>
            <Typography variant="overline" color="secondary.main">
              The SaveSimple collection
            </Typography>
            <Typography variant="h1">Organization, made effortless.</Typography>
            <Typography variant="h5" component="p" color="text.secondary" sx={{ maxWidth: 620 }}>
              Streamlined checklists, planners, and printables that make staying organized easy and
              effortless. Instant PDFs from RealKevbase — with small-run merch in the same cart.
            </Typography>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
              <Button size="large" variant="contained" href="#products" endIcon={<ArrowForwardRoundedIcon />}>
                Shop the collection
              </Button>
              <Button size="large" variant="outlined" href="#how-it-works">
                How it works
              </Button>
            </Stack>
            <Stack direction="row" spacing={2.5} sx={{ pt: 1, color: 'text.secondary', flexWrap: 'wrap' }}>
              <Typography variant="caption">24-hour download links</Typography>
              <Typography variant="caption">Stripe checkout</Typography>
              <Typography variant="caption">Ships worldwide</Typography>
            </Stack>
          </Stack>
        </Container>
      </Box>

      <ProductGrid products={products} loading={loading} error={error} onRetry={onRetry} />

      <Box
        component="section"
        id="how-it-works"
        sx={{ py: { xs: 7, md: 10 }, bgcolor: '#F5F2EA', scrollMarginTop: { xs: '72px', md: '88px' } }}
      >
        <Container maxWidth="lg">
          <Stack spacing={1} sx={{ mb: { xs: 4, md: 5 }, maxWidth: 560 }}>
            <Typography variant="overline" color="secondary.main">
              Why buy here
            </Typography>
            <Typography variant="h2">Digital speed, physical quality.</Typography>
          </Stack>

          <Grid container spacing={{ xs: 2, md: 3 }}>
            {VALUE_PROPS.map(({ icon: Icon, title, copy }) => (
              <Grid key={title} size={{ xs: 12, sm: 6, md: 4 }}>
                <Stack
                  spacing={1.25}
                  sx={{
                    p: 3,
                    height: '100%',
                    bgcolor: 'background.paper',
                    borderRadius: 3,
                    border: '1px solid',
                    borderColor: 'divider',
                  }}
                >
                  <Box
                    aria-hidden="true"
                    sx={{
                      width: 44,
                      height: 44,
                      borderRadius: 2,
                      display: 'grid',
                      placeItems: 'center',
                      bgcolor: '#EEF2FF',
                      color: '#1D4ED8',
                    }}
                  >
                    <Icon fontSize="small" />
                  </Box>
                  <Typography variant="h6" component="h3">
                    {title}
                  </Typography>
                  <Typography variant="body2" color="text.secondary">
                    {copy}
                  </Typography>
                </Stack>
              </Grid>
            ))}
          </Grid>
        </Container>
      </Box>
    </>
  )
}

export default HomePage
