import { useEffect } from 'react'

import { Box, Button, Container, Grid, Stack, Typography } from '@mui/material'
import ArrowForwardRoundedIcon from '@mui/icons-material/ArrowForwardRounded'
import BoltRoundedIcon from '@mui/icons-material/BoltRounded'
import LocalShippingOutlinedIcon from '@mui/icons-material/LocalShippingOutlined'
import LockOutlinedIcon from '@mui/icons-material/LockOutlined'

import { ProductGrid } from './ProductGrid.jsx'
import { Reviews } from './Reviews.jsx'
import { HOME_HERO } from '../data/home.js'
import { HUB_LINKS } from '../data/hubLinks.js'
import { ACCENT_WASH } from '../data/site.js'
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
    title: 'Printed to order',
    copy: 'Every piece is produced after you order it, then shipped. PDFs in the same order download immediately.',
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
              `radial-gradient(60% 60% at 72% 0%, ${ACCENT_WASH}, transparent 62%)`,
          }}
        />
        <Container maxWidth="lg" sx={{ position: 'relative', pt: { xs: 8, md: 12 }, pb: { xs: 2, md: 2.5 } }}>
          <Stack spacing={3} sx={{ maxWidth: 900 }}>
            <Typography variant="overline" color="secondary.main">
              {HOME_HERO.overline}
            </Typography>
            {/*
              54px in a 900px column, balanced: the theme's h1 tops out at 68px, which
              pushed this headline onto three lines. `balance` splits it evenly instead
              of leaving a word alone on the last line.
            */}
            <Typography
              variant="h1"
              sx={{ fontSize: 'clamp(2.3rem, 6vw, 3.4rem)', textWrap: 'balance' }}
            >
              {HOME_HERO.title}
            </Typography>
            <Typography variant="h5" component="p" color="text.secondary" sx={{ maxWidth: 620 }}>
              {HOME_HERO.subtitle}
            </Typography>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
              <Button
                size="large"
                variant="contained"
                href={HOME_HERO.cta.href}
                endIcon={<ArrowForwardRoundedIcon />}
              >
                {HOME_HERO.cta.label}
              </Button>
            </Stack>
            <Stack direction="row" spacing={2.5} sx={{ pt: 1, color: 'text.secondary', flexWrap: 'wrap' }}>
              {HOME_HERO.captions.map((caption) => (
                <Typography key={caption} variant="caption">
                  {caption}
                </Typography>
              ))}
            </Stack>
            {/*
              One link per collection. These are plain anchors, not router links: the hubs
              are prerendered HTML with no SPA route, so a `Link` would fall through to the
              catch-all redirect. The box model here is repeated in `homeSeed()` so the
              gallery below does not shift when React replaces the seeded hero.
            */}
            <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', pt: 2 }}>
              {HUB_LINKS.map((hub) => (
                <Box
                  key={hub.slug}
                  component="a"
                  href={`/${hub.slug}`}
                  sx={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    height: 34,
                    px: 1.75,
                    borderRadius: '999px',
                    border: '1px solid',
                    borderColor: 'divider',
                    bgcolor: 'background.paper',
                    color: 'text.primary',
                    fontSize: 13,
                    fontWeight: 600,
                    textDecoration: 'none',
                    whiteSpace: 'nowrap',
                    '&:hover': { borderColor: 'text.primary' },
                  }}
                >
                  {hub.label}
                </Box>
              ))}
            </Stack>
          </Stack>
        </Container>
      </Box>

      <ProductGrid products={products} loading={loading} error={error} onRetry={onRetry} />

      <Reviews />

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
