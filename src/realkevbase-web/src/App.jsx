import { useEffect } from 'react'

import { Box, Container, Stack, Typography } from '@mui/material'
import { ThemeProvider } from '@mui/material/styles'
import CssBaseline from '@mui/material/CssBaseline'
import { Navigate, Route, Routes, useLocation } from 'react-router-dom'

import { CartProvider } from './cart/CartProvider.jsx'
import { CartDrawer } from './components/CartDrawer.jsx'
import { CheckoutSuccess } from './components/CheckoutSuccess.jsx'
import { GuidePage } from './components/GuidePage.jsx'
import { HomePage } from './components/HomePage.jsx'
import { Navbar } from './components/Navbar.jsx'
import { theme } from './theme.js'
import { useCatalog } from './hooks/useCatalog.js'

/**
 * Keeps the viewport sensible across client-side navigation: jumps to top on a new page
 * and honours `#section` links from the navbar when one is present.
 *
 * @returns {null} Renders nothing.
 */
function ScrollManager() {
  const { pathname, hash } = useLocation()

  useEffect(() => {
    if (hash) {
      const target = document.getElementById(hash.slice(1))
      if (target) {
        target.scrollIntoView({ behavior: 'smooth', block: 'start' })
        return
      }
    }
    window.scrollTo(0, 0)
  }, [pathname, hash])

  return null
}

/** Slim site footer shared by every route. */
function SiteFooter() {
  return (
    <Box component="footer" sx={{ borderTop: '1px solid', borderColor: 'divider', py: 4 }}>
      <Container maxWidth="lg">
        <Stack
          direction={{ xs: 'column', sm: 'row' }}
          spacing={1}
          sx={{ justifyContent: 'space-between', alignItems: { xs: 'flex-start', sm: 'center' } }}
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
  )
}

/**
 * Storefront shell: theme, shared cart provider, header, routed pages, footer and the
 * slide-out cart. The catalog is loaded once here so both routes share one request.
 *
 * @returns {React.JSX.Element} The application.
 */
function App() {
  const catalog = useCatalog()

  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <CartProvider>
        <ScrollManager />
        <Box
          id="top"
          sx={{
            minHeight: '100vh',
            display: 'flex',
            flexDirection: 'column',
            bgcolor: 'background.default',
          }}
        >
          <Navbar />

          <Box component="main" sx={{ flexGrow: 1 }}>
            <CheckoutSuccess />

            <Routes>
              <Route
                path="/"
                element={
                  <HomePage
                    products={catalog.products}
                    loading={catalog.loading}
                    error={catalog.error}
                    onRetry={catalog.reload}
                  />
                }
              />
              <Route path="/guides/:slug" element={<GuidePage catalog={catalog} />} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </Box>

          <SiteFooter />

          <CartDrawer />
        </Box>
      </CartProvider>
    </ThemeProvider>
  )
}

export default App
