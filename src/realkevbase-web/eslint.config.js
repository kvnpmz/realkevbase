import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores(['dist', '.wrangler']),
  {
    // JavaScript only, on purpose. The SPA is TypeScript, and linting `.ts`/`.tsx` needs
    // typescript-eslint, whose current release peers on `typescript <6.1.0` — it cannot be
    // installed beside TypeScript 7 without `--legacy-peer-deps`. Until it can, the SPA is
    // covered by `tsc --noEmit` (which includes noUnusedLocals/noUnusedParameters) and by
    // nothing else, so `react-hooks/exhaustive-deps` does not run. See docs/ts-migration.md.
    files: ['**/*.{js,mjs,cjs,jsx}'],
    extends: [
      js.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      globals: globals.browser,
      parserOptions: { ecmaFeatures: { jsx: true } },
    },
  },
  {
    // Build and tooling scripts run under Node, so `process` is a global there. They used
    // to sit outside the `files` list entirely, which is how `process` stayed unnoticed.
    files: ['scripts/**/*.{js,mjs,cjs}'],
    languageOptions: { globals: globals.node },
  },
])
