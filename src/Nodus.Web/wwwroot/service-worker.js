// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development difficult (changes wouldn't be reflected immediately).
// In production, we'll want to cache resources.
const CACHE_NAME = "nodus-web-v1";
const OFFLINE_URL = "offline.html";

const ASSETS_TO_CACHE = [
  "./",
  "index.html",
  "manifest.json",
  "css/app.css", // Adjust based on actual CSS location
  "favicon.png",
  "icons/icon-192.png",
  "icons/icon-512.png",
  "css/display.css",
  "https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css", 
  "_framework/blazor.webassembly.js",
  "_framework/blazor.boot.json",
  // We rely on Blazor's PWA template logic usually, but this is a manual addition
  // to ensure basic offline capability if the template wasn't fully set up.
];

self.addEventListener("install", (event) => {
  self.skipWaiting();
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => {
      return cache.addAll(ASSETS_TO_CACHE).catch((err) => {
        console.warn("Failed to cache some assets", err);
      });
    }),
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys().then((cacheNames) => {
      return Promise.all(
        cacheNames
          .filter((name) => name !== CACHE_NAME)
          .map((name) => caches.delete(name)),
      );
    }),
  );
  self.clients.claim();
});

self.addEventListener("fetch", (event) => {
  // For navigation requests, try network first, fall back to cache
  if (event.request.mode === "navigate") {
    event.respondWith(
      fetch(event.request).catch(() => {
        return caches.match(event.request).then((response) => {
          if (response) return response;
          // Fallback to index.html for SPA routing
          return caches.match("index.html");
        });
      }),
    );
    return;
  }

  // For other requests, try cache first, then network
  event.respondWith(
    caches.match(event.request).then((cachedResponse) => {
      return cachedResponse || fetch(event.request);
    }),
  );
});
