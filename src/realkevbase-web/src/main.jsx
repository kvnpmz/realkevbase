import { StrictMode } from 'react'
import { flushSync } from 'react-dom'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from './App.jsx'
import { initAnalytics } from './analytics/ga'

// Loads Google Analytics only when VITE_GA_ID was set at build time.
initAnalytics()

const root = createRoot(document.getElementById('root'))

// Guide pages ship a static product block and `/` ships its hero, so something real is on
// screen while this bundle is still downloading. Now that it is running, drop them: React
// renders the real page, and a leftover seed would show the same hero or product twice.
//
// `flushSync` is not decoration. Removing the seeds and letting React schedule the first
// render leaves a frame in which the document has no content at all, so the footer that
// sat below the hero jumps to the top of the viewport and back — two full-screen layout
// shifts, 0.27 of CLS, for a swap the reader should never see. Flushing commits the first
// render inside this task, so the browser lays out once.
flushSync(() => {
  for (const seed of document.querySelectorAll('[data-product-seed], [data-home-seed]')) {
    seed.remove()
  }

  root.render(
    <StrictMode>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </StrictMode>,
  )
})
