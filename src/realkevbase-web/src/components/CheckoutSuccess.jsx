import { useEffect, useRef, useState } from 'react'

import { Alert, Box, Button, Container, Divider, Paper, Stack, Typography } from '@mui/material'
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded'
import DownloadRoundedIcon from '@mui/icons-material/DownloadRounded'
import HourglassTopRoundedIcon from '@mui/icons-material/HourglassTopRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import StorefrontOutlinedIcon from '@mui/icons-material/StorefrontOutlined'

import { checkoutApi as defaultCheckoutApi } from '../api/checkoutApi.js'
import { trackPurchase } from '../analytics/ga.js'

/**
 * Stages the success panel can be in.
 * @readonly
 * @enum {string}
 */
const STAGE = Object.freeze({
  LOADING: 'loading',
  READY: 'ready',
  MERCH_ONLY: 'merch-only',
  SETTLING: 'settling',
  MISSING: 'missing',
  ERROR: 'error',
})

/**
 * Formats a link expiry for display.
 * @param {string | null} value ISO timestamp from the API.
 * @returns {string} Localised date and time, or a generic fallback.
 */
function formatExpiry(value) {
  if (!value) return 'within 24 hours'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return 'within 24 hours'
  return `by ${new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(date)}`
}

/**
 * Success page for a completed checkout.
 *
 * Stripe redirects the buyer back with `?checkout=success&session_id=cs_…`. That id is the
 * only capability in the URL: the backend re-verifies payment with Stripe on every call, so
 * nothing here can be forged by editing the query string. Renders nothing at all when the
 * visitor did not just check out.
 *
 * @param {{
 *   api?: ReturnType<typeof import('../api/checkoutApi').createCheckoutApi>,
 *   search?: string,
 * }} [props] Component props; both overridable for tests.
 * @returns {React.JSX.Element | null} The panel, or null when not on a success redirect.
 */
export function CheckoutSuccess({ api = defaultCheckoutApi, search } = {}) {
  const query = search ?? (typeof window === 'undefined' ? '' : window.location.search)
  const params = typeof URLSearchParams === 'function' ? new URLSearchParams(query) : null
  const isSuccess = params?.get('checkout') === 'success'
  const sessionId = params?.get('session_id')?.trim() ?? ''

  const [state, setState] = useState({
    stage: sessionId ? STAGE.LOADING : STAGE.MISSING,
    downloads: [],
    message: '',
  })

  /** Bumped by the retry button to re-run the request. */
  const [attempt, setAttempt] = useState(0)

  /** Ensures the purchase event is sent once even if the panel re-renders. */
  const purchaseTracked = useRef(false)

  useEffect(() => {
    if (purchaseTracked.current) return
    if (state.stage !== STAGE.READY && state.stage !== STAGE.MERCH_ONLY) return
    if (!sessionId) return

    purchaseTracked.current = true
    trackPurchase(sessionId)
  }, [sessionId, state.stage])

  useEffect(() => {
    if (!isSuccess || !sessionId) return undefined

    const controller = new AbortController()

    api
      .getSessionDownloads(sessionId, { signal: controller.signal })
      .then((result) => {
        if (controller.signal.aborted) return
        setState({
          stage: result.downloads.length > 0 ? STAGE.READY : STAGE.MERCH_ONLY,
          downloads: result.downloads,
          message: '',
        })
      })
      .catch((error) => {
        if (controller.signal.aborted || (error && error.name === 'AbortError')) return

        if (error && error.status === 409) {
          setState({ stage: STAGE.SETTLING, downloads: [], message: '' })
          return
        }

        setState({
          stage: STAGE.ERROR,
          downloads: [],
          message:
            error instanceof Error && error.message
              ? error.message
              : 'We could not load your downloads. Please try again.',
        })
      })

    return () => controller.abort()
  }, [api, sessionId, isSuccess, attempt])

  /**
   * Re-runs the request from a user action (retry after 409, or after a failure).
   * @returns {void}
   */
  const retry = () => {
    setState({ stage: STAGE.LOADING, downloads: [], message: '' })
    setAttempt((value) => value + 1)
  }

  if (!isSuccess) return null

  return (
    <Box
      component="section"
      aria-live="polite"
      sx={{
        pt: { xs: 6, md: 9 },
        pb: { xs: 3, md: 4 },
        background:
          'radial-gradient(70% 120% at 50% 0%, rgba(47, 107, 255, 0.10), transparent 70%)',
      }}
    >
      <Container maxWidth="md">
        <Paper
          sx={{
            p: { xs: 3, sm: 4, md: 5 },
            borderColor: 'rgba(47, 107, 255, 0.24)',
            boxShadow: '0 24px 60px -40px rgba(20, 22, 26, 0.45)',
          }}
        >
          <Stack spacing={3}>
            <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center' }}>
              <Box
                aria-hidden="true"
                sx={{
                  width: 48,
                  height: 48,
                  borderRadius: '50%',
                  display: 'grid',
                  placeItems: 'center',
                  bgcolor: '#EAF7F1',
                  color: '#0F766E',
                  flexShrink: 0,
                }}
              >
                <CheckCircleRoundedIcon />
              </Box>
              <Box>
                <Typography variant="overline" color="secondary.main">
                  Order confirmed
                </Typography>
                <Typography variant="h2" sx={{ fontSize: { xs: '1.6rem', sm: '2rem' } }}>
                  {state.stage === STAGE.MERCH_ONLY ? 'Thanks for your order' : 'Your downloads'}
                </Typography>
              </Box>
            </Stack>

            {state.stage === STAGE.MISSING && (
              <Alert severity="warning" variant="outlined">
                This link is missing its order reference. Open the download link from your Stripe
                receipt, or contact support with your order email.
              </Alert>
            )}

            {state.stage === STAGE.LOADING && (
              <Stack spacing={1.5} aria-busy="true">
                <Box sx={{ height: 20, width: '55%', borderRadius: 1, bgcolor: '#F0EEE8' }} />
                <Box sx={{ height: 20, width: '40%', borderRadius: 1, bgcolor: '#F0EEE8' }} />
                <Typography variant="body2" color="text.secondary">
                  Preparing your secure links…
                </Typography>
              </Stack>
            )}

            {state.stage === STAGE.SETTLING && (
              <Stack spacing={2}>
                <Alert
                  severity="info"
                  variant="outlined"
                  icon={<HourglassTopRoundedIcon fontSize="small" />}
                >
                  Payment is still settling, so your files aren&apos;t ready yet. This is normal for
                  some payment methods and usually clears within a minute.
                </Alert>
                <Box>
                  <Button
                    variant="contained"
                    startIcon={<RefreshRoundedIcon />}
                    onClick={retry}
                  >
                    Refresh in a moment
                  </Button>
                </Box>
              </Stack>
            )}

            {state.stage === STAGE.ERROR && (
              <Stack spacing={2}>
                <Alert severity="error" variant="outlined">
                  {state.message}
                </Alert>
                <Box>
                  <Button variant="outlined" startIcon={<RefreshRoundedIcon />} onClick={retry}>
                    Try again
                  </Button>
                </Box>
              </Stack>
            )}

            {state.stage === STAGE.MERCH_ONLY && (
              <Stack spacing={2}>
                <Alert severity="success" variant="outlined" icon={<StorefrontOutlinedIcon fontSize="small" />}>
                  Your order is on its way. Physical items are printed and shipped within 2–4
                  business days, and tracking lands in your inbox.
                </Alert>
                <Typography variant="body2" color="text.secondary">
                  Nothing to download for this order.
                </Typography>
              </Stack>
            )}

            {state.stage === STAGE.READY && (
              <Stack spacing={2.5}>
                <Typography variant="body1" color="text.secondary">
                  Download your files now. Each link is private to this order and expires 24 hours
                  after checkout.
                </Typography>

                <Stack divider={<Divider flexItem />} spacing={2}>
                  {state.downloads.map((download) => (
                    <Stack
                      key={download.productId || download.url}
                      direction={{ xs: 'column', sm: 'row' }}
                      spacing={1.5}
                      sx={{
                        justifyContent: 'space-between',
                        alignItems: { xs: 'flex-start', sm: 'center' },
                      }}
                    >
                      <Box sx={{ minWidth: 0 }}>
                        <Typography variant="h6" component="p">
                          {download.name}
                        </Typography>
                        <Typography variant="caption" color="text.secondary">
                          {download.edition ? `${download.edition} PDF` : 'PDF'} · link valid{' '}
                          {formatExpiry(download.expiresAt)}
                        </Typography>
                      </Box>
                      <Button
                        component="a"
                        href={download.url}
                        target="_blank"
                        rel="noopener noreferrer"
                        variant="contained"
                        startIcon={<DownloadRoundedIcon />}
                        sx={{ flexShrink: 0 }}
                      >
                        Download {download.edition ?? 'PDF'}
                      </Button>
                    </Stack>
                  ))}
                </Stack>

                <Box
                  sx={{
                    p: 2,
                    borderRadius: 2,
                    bgcolor: '#FBFAF7',
                    border: '1px solid',
                    borderColor: 'divider',
                  }}
                >
                  <Typography variant="caption" color="text.secondary">
                    Links expire in 24 hours. Need them later? Open this page again from your Stripe
                    receipt before they lapse, and fresh links are issued.
                  </Typography>
                </Box>

                <Box>
                  <Button variant="text" href="#products">
                    Keep browsing
                  </Button>
                </Box>
              </Stack>
            )}
          </Stack>
        </Paper>
      </Container>
    </Box>
  )
}

export default CheckoutSuccess
