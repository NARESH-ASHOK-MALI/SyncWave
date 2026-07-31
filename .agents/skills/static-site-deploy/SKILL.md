---
name: static-site-deploy
description: Use when building, testing, or deploying the SyncWave landing page to GitHub Pages.
---

# Deploy Conventions

- vite.config.js base path must match the GitHub Pages repo name exactly.
- Build with `npm run build`, deploy with `npx gh-pages -d dist`.
- Screenshots live in `public/screenshots/` — filenames must have no spaces.
- Verify the download link on the CTA points at the latest GitHub Release
  asset, not a hardcoded old version.
