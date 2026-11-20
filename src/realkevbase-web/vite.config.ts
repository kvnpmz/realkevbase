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
      // Guide covers are copied into the deployment by `scripts/generate-seo.mjs`, which a
      // dev server never runs, so `/art/<id>.webp` resolves to the API's own thumbnail route
      // instead. Development sees the same picture; production sees a static file.
      '/art': {
        target: 'https://api.realkevbase.com',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/art\/([^./]+)\.webp$/, '/api/products/$1/thumbnail'),
      },
    },
  },
})
