import { Box, Button, Container, Grid, Stack, Typography } from '@mui/material'
import { ThemeProvider } from '@mui/material/styles'
import CssBaseline from '@mui/material/CssBaseline'
import ArrowForwardRoundedIcon from '@mui/icons-material/ArrowForwardRounded'
import BoltRoundedIcon from '@mui/icons-material/BoltRounded'
import LocalShippingOutlinedIcon from '@mui/icons-material/LocalShippingOutlined'
import LockOutlinedIcon from '@mui/icons-material/LockOutlined'

import { CartProvider } from './cart/CartProvider.jsx'
import { CartDrawer } from './components/CartDrawer.jsx'
import { CheckoutSuccess } from './components/CheckoutSuccess.jsx'
import { Navbar } from './components/Navbar.jsx'
import { ProductGrid } from './components/ProductGrid.jsx'
import { demoProducts } from './data/products.js'
import { theme } from './theme.js'

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
 * Storefront shell: theme, shared cart provider, header, hero, product grid,
 * reassurance section, footer and the slide-out cart.
 *
 * @returns {React.JSX.Element} The application.
 */
function App() {
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <CartProvider>
        <Box id="top" sx={{ minHeight: '100vh', bgcolor: 'background.default' }}>
          <Navbar />

          <Box component="main">
            <CheckoutSuccess />

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
                  <Typography
                    variant="h5"
                    component="p"
                    color="text.secondary"
                    sx={{ maxWidth: 620 }}
                  >
                    Streamlined checklists, planners, and printables that make staying organized
                    easy and effortless. Instant PDFs from RealKevbase — with small-run merch in
                    the same cart.
                  </Typography>
                  <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
                    <Button
                      size="large"
                      variant="contained"
                      href="#products"
                      endIcon={<ArrowForwardRoundedIcon />}
                    >
                      Shop the collection
                    </Button>
                    <Button size="large" variant="outlined" href="#how-it-works">
                      How it works
                    </Button>
                  </Stack>
                  <Stack
                    direction="row"
                    spacing={2.5}
                    sx={{ pt: 1, color: 'text.secondary', flexWrap: 'wrap' }}
                  >
                    <Typography variant="caption">24-hour download links</Typography>
                    <Typography variant="caption">Stripe checkout</Typography>
                    <Typography variant="caption">Ships worldwide</Typography>
                  </Stack>
                </Stack>
              </Container>
            </Box>

            <ProductGrid products={demoProducts} />

            <Box
              component="section"
              id="how-it-works"
              sx={{
                py: { xs: 7, md: 10 },
                bgcolor: '#F5F2EA',
                scrollMarginTop: { xs: '72px', md: '88px' },
              }}
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
          </Box>

          <Box component="footer" sx={{ borderTop: '1px solid', borderColor: 'divider', py: 4 }}>
            <Container maxWidth="lg">
              <Stack
                direction={{ xs: 'column', sm: 'row' }}
                spacing={1}
                sx={{
                  justifyContent: 'space-between',
                  alignItems: { xs: 'flex-start', sm: 'center' },
                }}
              >
                <Typography variant="body2" color="text.secondary">
                  © {new Date().getFullYear()} RealKevbase. All rights reserved.
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  PDF downloads use secure 24-hour links. Payments processed by Stripe.
                </Typography>
              </Stack>
            </Container>
          </Box>

          <CartDrawer />
        </Box>
      </CartProvider>
    </ThemeProvider>
  )
}

export default App
