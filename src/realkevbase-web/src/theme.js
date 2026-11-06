import { createTheme } from '@mui/material/styles'

/**
 * Storefront theme: warm, high-contrast, conversion-focused.
 * System fonts keep first paint instant; the palette pairs near-black "ink" with
 * a single trustworthy blue accent and an off-white canvas.
 */
const ink = '#14161A'
const accent = '#2F6BFF'

export const theme = createTheme({
  palette: {
    mode: 'light',
    primary: { main: ink, contrastText: '#FFFFFF' },
    secondary: { main: accent, contrastText: '#FFFFFF' },
    background: { default: '#FBFAF7', paper: '#FFFFFF' },
    text: { primary: ink, secondary: '#5A6068' },
    divider: '#E7E4DC',
  },
  shape: { borderRadius: 14 },
  typography: {
    fontFamily:
      "system-ui, -apple-system, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif",
    h1: {
      fontWeight: 800,
      letterSpacing: '-0.035em',
      lineHeight: 1.03,
      fontSize: 'clamp(2.6rem, 6vw, 4.25rem)',
    },
    h2: {
      fontWeight: 800,
      letterSpacing: '-0.025em',
      lineHeight: 1.1,
      fontSize: 'clamp(1.8rem, 3.6vw, 2.6rem)',
    },
    h5: { fontWeight: 400, lineHeight: 1.5 },
    h6: { fontWeight: 700, letterSpacing: '-0.01em' },
    button: { textTransform: 'none', fontWeight: 700 },
    overline: { fontWeight: 700, letterSpacing: '0.12em' },
  },
  components: {
    MuiCssBaseline: {
      styleOverrides: {
        body: {
          backgroundColor: '#FBFAF7',
          color: ink,
          WebkitFontSmoothing: 'antialiased',
          MozOsxFontSmoothing: 'grayscale',
        },
        '::selection': { backgroundColor: 'rgba(47, 107, 255, 0.18)' },
      },
    },
    MuiButton: {
      defaultProps: { disableElevation: true },
      styleOverrides: {
        root: { borderRadius: 999, paddingInline: 20, paddingBlock: 9 },
        containedPrimary: { '&:hover': { backgroundColor: '#000000' } },
      },
    },
    MuiCard: {
      defaultProps: { elevation: 0 },
      styleOverrides: {
        root: { border: '1px solid #E7E4DC', borderRadius: 18, backgroundImage: 'none' },
      },
    },
    MuiChip: {
      styleOverrides: { root: { borderRadius: 999, fontWeight: 600 } },
    },
    MuiIconButton: {
      styleOverrides: { root: { borderRadius: 12 } },
    },
    MuiAppBar: {
      defaultProps: { elevation: 0 },
    },
    MuiDrawer: {
      styleOverrides: { paper: { backgroundImage: 'none' } },
    },
  },
})

export default theme
