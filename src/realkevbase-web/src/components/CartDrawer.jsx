import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Divider,
  Drawer,
  IconButton,
  Stack,
  Typography,
} from '@mui/material'
import AddRoundedIcon from '@mui/icons-material/AddRounded'
import ArrowForwardRoundedIcon from '@mui/icons-material/ArrowForwardRounded'
import CheckroomRoundedIcon from '@mui/icons-material/CheckroomRounded'
import CloseRoundedIcon from '@mui/icons-material/CloseRounded'
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded'
import LockOutlinedIcon from '@mui/icons-material/LockOutlined'
import MenuBookRoundedIcon from '@mui/icons-material/MenuBookRounded'
import RemoveRoundedIcon from '@mui/icons-material/RemoveRounded'
import ShoppingBagOutlinedIcon from '@mui/icons-material/ShoppingBagOutlined'

import { useCartContext } from '../cart/cartContext.js'
import { FULFILLMENT } from '../cart/cartReducer.js'
import { formatMoney } from '../cart/formatMoney.js'

/** @param {{ label: string, value: string, strong?: boolean, muted?: boolean }} props */
function SummaryRow({ label, value, strong = false, muted = false }) {
  return (
    <Stack direction="row" spacing={2} sx={{ justifyContent: 'space-between', alignItems: 'baseline' }}>
      <Typography
        variant={strong ? 'subtitle1' : 'body2'}
        sx={{ fontWeight: strong ? 800 : 500, color: muted ? 'text.secondary' : 'text.primary' }}
      >
        {label}
      </Typography>
      <Typography
        variant={strong ? 'subtitle1' : 'body2'}
        sx={{
          fontWeight: strong ? 800 : 500,
          color: muted ? 'text.secondary' : 'text.primary',
          textAlign: 'right',
        }}
      >
        {value}
      </Typography>
    </Stack>
  )
}

/**
 * A single cart line with quantity controls, line total and fulfillment hint.
 * @param {{ item: import('../cart/cartReducer.js').CartItem }} props Component props.
 * @returns {React.JSX.Element} The cart line.
 */
function CartLine({ item }) {
  const { increment, decrement, removeItem } = useCartContext()
  const isDigital = item.fulfillment === FULFILLMENT.DIGITAL_PDF

  return (
    <Box>
      <Stack direction="row" spacing={1.5} sx={{ alignItems: 'flex-start' }}>
        <Box
          aria-hidden="true"
          sx={{
            width: 52,
            height: 52,
            flexShrink: 0,
            borderRadius: 2,
            display: 'grid',
            placeItems: 'center',
            bgcolor: isDigital ? '#EEF2FF' : '#F6EFE3',
            color: isDigital ? '#1D4ED8' : '#92400E',
          }}
        >
          {isDigital ? <MenuBookRoundedIcon fontSize="small" /> : <CheckroomRoundedIcon fontSize="small" />}
        </Box>

        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Stack direction="row" spacing={1} sx={{ justifyContent: 'space-between' }}>
            <Typography variant="subtitle2" noWrap sx={{ fontWeight: 700 }}>
              {item.name}
            </Typography>
            <Typography variant="subtitle2" sx={{ fontWeight: 700, whiteSpace: 'nowrap' }}>
              {formatMoney(item.unitPrice * item.quantity)}
            </Typography>
          </Stack>

          <Typography variant="caption" color="text.secondary">
            {isDigital ? 'Digital PDF · 24-hour download link' : 'Physical merch · ships in 2–4 days'}
          </Typography>

          <Stack direction="row" sx={{ mt: 1, alignItems: 'center', justifyContent: 'space-between' }}>
            <Stack
              direction="row"
              sx={{ alignItems: 'center', border: '1px solid', borderColor: 'divider', borderRadius: 999, px: 0.25 }}
            >
              <IconButton
                size="small"
                onClick={() => decrement(item.id)}
                disabled={item.quantity <= 1}
                aria-label={`Decrease quantity of ${item.name}`}
              >
                <RemoveRoundedIcon fontSize="inherit" />
              </IconButton>
              <Typography
                component="span"
                aria-live="polite"
                sx={{ minWidth: 24, textAlign: 'center', fontSize: 14, fontWeight: 700 }}
              >
                {item.quantity}
              </Typography>
              <IconButton
                size="small"
                onClick={() => increment(item.id)}
                aria-label={`Increase quantity of ${item.name}`}
              >
                <AddRoundedIcon fontSize="inherit" />
              </IconButton>
            </Stack>

            <IconButton
              size="small"
              onClick={() => removeItem(item.id)}
              aria-label={`Remove ${item.name} from cart`}
            >
              <DeleteOutlineRoundedIcon fontSize="small" />
            </IconButton>
          </Stack>
        </Box>
      </Stack>
      <Divider sx={{ mt: 2 }} />
    </Box>
  )
}

/**
 * Slide-out cart. Shows every selected line (PDFs and merch together), live totals and a
 * checkout button wired to the shared cart state and the Stripe session endpoint.
 *
 * @returns {React.JSX.Element} The cart drawer.
 */
export function CartDrawer() {
  const {
    items,
    totals,
    isEmpty,
    isCartOpen,
    closeCart,
    clearCart,
    checkout,
    checkoutStatus,
    checkoutError,
  } = useCartContext()

  const isSubmitting = checkoutStatus === 'submitting'

  return (
    <Drawer
      anchor="right"
      open={isCartOpen}
      onClose={closeCart}
      aria-labelledby="cart-drawer-title"
      slotProps={{
        paper: {
          sx: { width: { xs: '100%', sm: 420 }, display: 'flex', flexDirection: 'column' },
        },
      }}
    >
      <Stack
        direction="row"
        spacing={1}
        sx={{ px: 2.5, py: 2, alignItems: 'center', borderBottom: '1px solid', borderColor: 'divider' }}
      >
        <Typography id="cart-drawer-title" variant="h6" component="h2" sx={{ fontWeight: 800 }}>
          Your cart
        </Typography>
        <Chip
          size="small"
          label={totals.itemCount}
          sx={{ bgcolor: 'rgba(20, 22, 26, 0.06)' }}
        />
        <Box sx={{ flexGrow: 1 }} />
        {!isEmpty && (
          <Button size="small" color="inherit" onClick={clearCart} sx={{ color: 'text.secondary' }}>
            Clear
          </Button>
        )}
        <IconButton onClick={closeCart} aria-label="Close cart">
          <CloseRoundedIcon />
        </IconButton>
      </Stack>

      <Box sx={{ flexGrow: 1, overflowY: 'auto', px: 2.5, py: 2.5 }}>
        {isEmpty ? (
          <Stack
            spacing={2}
            sx={{
              textAlign: 'center',
              height: '100%',
              minHeight: 320,
              color: 'text.secondary',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            <Box
              aria-hidden="true"
              sx={{
                width: 64,
                height: 64,
                borderRadius: '50%',
                bgcolor: 'rgba(20, 22, 26, 0.05)',
                display: 'grid',
                placeItems: 'center',
              }}
            >
              <ShoppingBagOutlinedIcon />
            </Box>
            <Box>
              <Typography variant="subtitle1" sx={{ fontWeight: 700, color: 'text.primary' }}>
                Your cart is empty
              </Typography>
              <Typography variant="body2">
                Add a guide or some merch — both live in the same order.
              </Typography>
            </Box>
            <Button
              component="a"
              href="#products"
              variant="contained"
              onClick={closeCart}
              endIcon={<ArrowForwardRoundedIcon />}
            >
              Browse the catalog
            </Button>
          </Stack>
        ) : (
          <Stack spacing={2}>
            {items.map((item) => (
              <CartLine key={item.id} item={item} />
            ))}
          </Stack>
        )}
      </Box>

      {!isEmpty && (
        <Box
          sx={{
            px: 2.5,
            py: 2.5,
            borderTop: '1px solid',
            borderColor: 'divider',
            bgcolor: 'background.paper',
          }}
        >
          <Stack spacing={1}>
            <SummaryRow label="Subtotal" value={formatMoney(totals.subtotal)} />
            {totals.hasDigitalPdf && (
              <SummaryRow
                label="PDF downloads"
                value={formatMoney(totals.digitalSubtotal)}
                muted
              />
            )}
            {totals.hasPhysicalMerch && (
              <SummaryRow label="Merchandise" value={formatMoney(totals.merchSubtotal)} muted />
            )}
            {totals.requiresShipping && (
              <SummaryRow label="Shipping" value="Calculated at checkout" muted />
            )}
            <Divider sx={{ my: 0.5 }} />
            <SummaryRow label="Total" value={formatMoney(totals.subtotal)} strong />
          </Stack>

          {checkoutError && (
            <Alert severity="error" sx={{ mt: 2 }}>
              {checkoutError.message}
            </Alert>
          )}

          <Button
            fullWidth
            size="large"
            variant="contained"
            onClick={() => checkout()}
            disabled={isEmpty || isSubmitting}
            startIcon={
              isSubmitting ? (
                <CircularProgress size={18} color="inherit" />
              ) : (
                <LockOutlinedIcon fontSize="small" />
              )
            }
            endIcon={isSubmitting ? undefined : <ArrowForwardRoundedIcon />}
            sx={{ mt: 2, py: 1.4 }}
          >
            {isSubmitting
              ? 'Starting secure checkout…'
              : `Checkout · ${formatMoney(totals.subtotal)}`}
          </Button>

          <Typography
            variant="caption"
            color="text.secondary"
            sx={{ display: 'block', mt: 1.25, textAlign: 'center' }}
          >
            Secure payment powered by Stripe
          </Typography>
        </Box>
      )}
    </Drawer>
  )
}

export default CartDrawer
