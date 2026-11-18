import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // The storefront asks for product artwork on its own origin, which in production is the
    // Pages Function at /api/*. `npm run dev` would otherwise 404 every cover, so the same
    // prefix is proxied to the API here and both paths behave alike.
    proxy: {
      '/api': {
        target: 'https://api.realkevbase.com',
        changeOrigin: true,
      },
    },
  },
})
