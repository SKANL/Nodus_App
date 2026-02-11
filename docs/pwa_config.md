# 📱 Nodus PWA Configuration Guide

Nodus.Web is designed as a Progressive Web App (PWA) to ensure offline capability for judges and staff. This guide details the configuration and behavior of the PWA components.

## Key Components

### 1. Manifest (`manifest.json`)

Located in `wwwroot/manifest.json`. Defines:

- **Display Mode:** `standalone` (Feels like a native app)
- **Theme Color:** `#667eea` (Matches brand)
- **Icons:** Required for home screen installation (192x192, 512x512)

### 2. Service Worker (`service-worker.js`)

Handles caching and offline requests.

- **Cache Strategy:**
  - **Static Assets:** Cache-First (Performance)
  - **API Requests:** Network-Only (Data freshness) - _Note: Offline data handled by `DatabaseService` (IndexedDB/SQLite)_
  - **Navigation:** Network-First with Cache Fallback

### 3. Offline Indicator

The `OfflineIndicator.razor` component listens to `window.online` and `window.offline` events to provide real-time feedback to the user.

## Configuration Steps

### Enabling Offline Mode

1. **Service Worker Registration:**
   Ensure `index.html` registers the service worker:

   ```javascript
   if ("serviceWorker" in navigator) {
     navigator.serviceWorker.register("service-worker.js");
   }
   ```

2. **Asset Caching:**
   Update the `urlsToCache` array in `service-worker.js` when adding new static resources (CSS, JS, Images).

### Testing Offline

1. Open Chrome DevTools -> Application tab.
2. Go to **Service Workers**.
3. Check **"Offline"**.
4. Reload the page. The app should load from cache.

## Troubleshooting

- **App not installable:** Verify `manifest.json` has `start_url`, `icons`, and `display: standalone`. Ensure served over HTTPS.
- **Stale Content:** Update `CACHE_NAME` in `service-worker.js` to force cache invalidation on new deployments.
