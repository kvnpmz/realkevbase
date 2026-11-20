import { Box, Container, Grid, Stack, Typography } from '@mui/material'
import FormatQuoteRoundedIcon from '@mui/icons-material/FormatQuoteRounded'

import { REVIEWS } from '../data/reviews'

/**
 * Buyer quotes on the landing page.
 *
 * Rendered as a plain blockquote list with the source on every card, so a reader can tell
 * these were left on Etsy rather than here. No stars and no aggregate: see
 * `../data/reviews` for why the rating stays off the page.
 *
 * @returns {React.JSX.Element} The testimonials section.
 */
export function Reviews() {
  return (
    <Box component="section" id="reviews" sx={{ py: { xs: 7, md: 9 }, bgcolor: 'background.paper' }}>
      <Container maxWidth="lg">
        <Stack spacing={1} sx={{ mb: { xs: 4, md: 5 }, maxWidth: 640 }}>
          <Typography variant="overline" color="secondary.main">
            From our Etsy shop
          </Typography>
          <Typography variant="h2">What buyers said</Typography>
        </Stack>

        <Grid container spacing={{ xs: 2, md: 3 }}>
          {REVIEWS.map(({ quote, name, detail }) => (
            <Grid key={`${name}-${detail}`} size={{ xs: 12, md: 4 }}>
              <Stack
                component="blockquote"
                sx={{
                  m: 0,
                  p: 3,
                  height: '100%',
                  display: 'flex',
                  // `gap` rather than `spacing`: MUI's spacing rule sets a margin on every
                  // child, which outranks the `mt: auto` that pins the attribution down.
                  gap: 1.5,
                  bgcolor: '#FBFAF7',
                  borderRadius: 3,
                  border: '1px solid',
                  borderColor: 'divider',
                }}
              >
                <Box aria-hidden="true" sx={{ color: '#AEB6C4', lineHeight: 0 }}>
                  <FormatQuoteRoundedIcon />
                </Box>
                <Typography variant="body1" sx={{ color: 'text.primary' }}>
                  {quote}
                </Typography>
                {/* Pinned to the bottom so the three cards line their names up even when one
                    quote runs longer than the others. */}
                <Box sx={{ mt: 'auto' }}>
                  <Typography variant="body2" sx={{ fontWeight: 600 }}>
                    {name}
                  </Typography>
                  <Typography variant="caption" color="text.secondary">
                    {detail}
                  </Typography>
                </Box>
              </Stack>
            </Grid>
          ))}
        </Grid>
      </Container>
    </Box>
  )
}

export default Reviews
