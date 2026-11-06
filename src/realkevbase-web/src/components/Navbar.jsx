import { useState } from 'react'

import {
  AppBar,
  Badge,
  Box,
  Button,
  Container,
  Divider,
  IconButton,
  Menu,
  MenuItem,
  Stack,
  Toolbar,
  Typography,
} from '@mui/material'
import MenuRoundedIcon from '@mui/icons-material/MenuRounded'
import ShoppingBagOutlinedIcon from '@mui/icons-material/ShoppingBagOutlined'

import { useCartContext } from '../cart/cartContext.js'
import { formatMoney } from '../cart/formatMoney.js'

/** Primary navigation targets. */
const NAV_LINKS = [
  { label: 'Shop', href: '#products' },
  { label: 'How it works', href: '#how-it-works' },
]

/**
 * Sticky, responsive site header with the brand mark and a live cart summary.
 * Links collapse into a menu below the `md` breakpoint; the cart is always reachable.
 *
 * @returns {React.JSX.Element} The header.
 */
export function Navbar() {
  const { totals, openCart } = useCartContext()
  const [menuAnchor, setMenuAnchor] = useState(null)

  const isMenuOpen = Boolean(menuAnchor)
  const cartLabel =
    totals.itemCount > 0
      ? `Open cart, ${totals.itemCount} item${totals.itemCount === 1 ? '' : 's'}`
      : 'Open cart, empty'

  return (
    <AppBar
      position="sticky"
      sx={{
        bgcolor: 'rgba(251, 250, 247, 0.88)',
        backdropFilter: 'blur(12px)',
        borderBottom: '1px solid',
        borderColor: 'divider',
        color: 'text.primary',
      }}
    >
      <Container maxWidth="lg">
        <Toolbar disableGutters sx={{ minHeight: { xs: 64, md: 76 }, gap: { xs: 1, md: 2 } }}>
          <Stack
            direction="row"
            spacing={1.25}
            component="a"
            href="#top"
            sx={{
              alignItems: 'center',
              flexShrink: 0,
              textDecoration: 'none',
              color: 'inherit',
            }}
          >
            <Box
              aria-hidden="true"
              sx={{
                width: 34,
                height: 34,
                borderRadius: '10px',
                background: 'linear-gradient(135deg, #2F6BFF 0%, #7C3AED 100%)',
                color: '#fff',
                display: 'grid',
                placeItems: 'center',
                fontSize: 13,
                fontWeight: 800,
                letterSpacing: '-0.02em',
              }}
            >
              RK
            </Box>
            <Typography
              variant="h6"
              component="span"
              sx={{ fontWeight: 800, fontSize: { xs: '1.05rem', sm: '1.25rem' }, whiteSpace: 'nowrap' }}
            >
              RealKevbase
            </Typography>
          </Stack>

          <Stack
            direction="row"
            spacing={0.5}
            sx={{ display: { xs: 'none', md: 'flex' }, ml: 2 }}
          >
            {NAV_LINKS.map((link) => (
              <Button
                key={link.href}
                href={link.href}
                color="inherit"
                sx={{ color: 'text.secondary', '&:hover': { color: 'text.primary' } }}
              >
                {link.label}
              </Button>
            ))}
          </Stack>

          <Box sx={{ flexGrow: 1 }} />

          <Button
            onClick={openCart}
            aria-label={cartLabel}
            color="inherit"
            startIcon={
              <Badge
                badgeContent={totals.itemCount}
                color="secondary"
                overlap="circular"
                showZero={false}
              >
                <ShoppingBagOutlinedIcon fontSize="small" />
              </Badge>
            }
            sx={{
              display: { xs: 'none', sm: 'inline-flex' },
              bgcolor: 'background.paper',
              border: '1px solid',
              borderColor: 'divider',
              color: 'text.primary',
              pl: 1.75,
              '&:hover': { borderColor: 'text.primary', bgcolor: 'background.paper' },
            }}
          >
            Cart{totals.itemCount > 0 ? ` · ${formatMoney(totals.subtotal)}` : ''}
          </Button>

          <IconButton
            onClick={openCart}
            aria-label={cartLabel}
            sx={{ display: { xs: 'inline-flex', sm: 'none' } }}
          >
            <Badge badgeContent={totals.itemCount} color="secondary" overlap="circular">
              <ShoppingBagOutlinedIcon />
            </Badge>
          </IconButton>

          <IconButton
            onClick={(event) => setMenuAnchor(event.currentTarget)}
            aria-label="Open navigation menu"
            aria-haspopup="true"
            aria-expanded={isMenuOpen}
            sx={{ display: { xs: 'inline-flex', md: 'none' } }}
          >
            <MenuRoundedIcon />
          </IconButton>
        </Toolbar>
      </Container>

      <Menu
        anchorEl={menuAnchor}
        open={isMenuOpen}
        onClose={() => setMenuAnchor(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }}
        slotProps={{ paper: { sx: { minWidth: 200, mt: 1 } } }}
      >
        {NAV_LINKS.map((link) => (
          <MenuItem
            key={link.href}
            component="a"
            href={link.href}
            onClick={() => setMenuAnchor(null)}
          >
            {link.label}
          </MenuItem>
        ))}
        <Divider />
        <MenuItem component="a" href="#products" onClick={() => setMenuAnchor(null)}>
          Browse all products
        </MenuItem>
      </Menu>
    </AppBar>
  )
}

export default Navbar
