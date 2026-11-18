import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from './App.jsx'
import { initAnalytics } from './analytics/ga.js'

// Loads Google Analytics only when VITE_GA_ID was set at build time.
initAnalytics()

// Guide pages ship a static product block and `/` ships its hero, so something real is on
// screen while this bundle is still downloading. Now that it is running, drop them: React
// renders the real page, and a leftover seed would show the same hero or product twice.
for (const seed of document.querySelectorAll('[data-product-seed], [data-home-seed]')) {
  seed.remove()
}

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </StrictMode>,
)
