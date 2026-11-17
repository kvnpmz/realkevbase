import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from './App.jsx'
import { initAnalytics } from './analytics/ga.js'

// Loads Google Analytics only when VITE_GA_ID was set at build time.
initAnalytics()

// Guide pages ship a static product block so something is on screen while this bundle is
// still downloading. Now that it is running, drop it: React renders the real product, and
// leaving the seed would show two of them until the catalog call returns.
document.querySelector('[data-product-seed]')?.remove()

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </StrictMode>,
)
