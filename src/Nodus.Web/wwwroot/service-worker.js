// Service Worker para Nodus Web — estrategia offline-first
// - Shell de la app (HTML/CSS/JS/iconos) → cache-first
// - Requests de navegación → network-first con fallback a cache
// - API calls → network-only (datos en tiempo real)
// - Fuentes externas / CDN → stale-while-revalidate
const CACHE_NAME = "nodus-web-v3";

const SHELL_ASSETS = [
  "./",
  "index.html",
  "manifest.json",
  "favicon.png",
  "icon-192.png",
  "icons/icon.svg",
  "css/app.css",
  "css/display.css",
  "_framework/blazor.webassembly.js",
  "_framework/blazor.boot.json",
];

// External CDN resources to cache on first visit (fonts, icon CSS)
const EXTERNAL_ASSETS = [
  "https://fonts.googleapis.com/css2?family=Inter:wght@400;700&family=Outfit:wght@300;600;800&display=swap",
  "https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css",
];

self.addEventListener("install", (event) => {
  self.skipWaiting();
  event.waitUntil(
    caches.open(CACHE_NAME).then(async (cache) => {
      // Cache local shell assets
      await cache.addAll(SHELL_ASSETS).catch((err) => {
        console.warn("[SW] No se pudo cachear algunos assets del shell:", err);
      });
      // Cache external CDN resources (best-effort — may fail on first offline install)
      for (const url of EXTERNAL_ASSETS) {
        try {
          await cache.add(url);
        } catch (err) {
          console.warn("[SW] No se pudo cachear recurso externo:", url, err);
        }
      }
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
  const url = new URL(event.request.url);

  // API calls siempre van a la red (datos en tiempo real, sin caché)
  if (url.pathname.startsWith("/api/")) {
    event.respondWith(fetch(event.request));
    return;
  }

  // Fuentes externas y CDN → stale-while-revalidate
  const externalHosts = ["fonts.googleapis.com", "fonts.gstatic.com", "cdn.jsdelivr.net"];
  if (externalHosts.some((h) => url.hostname === h)) {
    event.respondWith(
      caches.match(event.request).then((cached) => {
        const fetchPromise = fetch(event.request).then((response) => {
          if (response.ok) {
            const copy = response.clone();
            caches.open(CACHE_NAME).then((cache) => cache.put(event.request, copy));
          }
          return response;
        });
        return cached || fetchPromise;
      }),
    );
    return;
  }

  // Requests de navegación: network-first, fallback a index.html (SPA routing)
  if (event.request.mode === "navigate") {
    event.respondWith(
      fetch(event.request).catch(() => {
        return caches.match("index.html").then((r) => r ?? Response.error());
      }),
    );
    return;
  }

  // Assets estáticos (_framework, CSS, imágenes): cache-first
  event.respondWith(
    caches.match(event.request).then((cached) => {
      if (cached) return cached;
      return fetch(event.request).then((response) => {
        // Cachear respuestas válidas de assets de la app
        if (response.ok && url.origin === self.location.origin) {
          const copy = response.clone();
          caches.open(CACHE_NAME).then((cache) => cache.put(event.request, copy));
        }
        return response;
      });
    }),
  );
});
