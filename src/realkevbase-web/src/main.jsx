import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App.jsx'
import { initAnalytics } from './analytics/ga.js'

// Loads Google Analytics only when VITE_GA_ID was set at build time.
initAnalytics()

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
