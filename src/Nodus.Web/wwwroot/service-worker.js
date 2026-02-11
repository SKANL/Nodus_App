const CACHE_NAME = 'nodus-v1';
const urlsToCache = [
  '/',
  '/css/app.css',
  '/css/display.css',
  '/lib/bootstrap/dist/css/bootstrap.min.css',
  '/manifest.json',
  '/favicon.png',
  '/_framework/blazor.webassembly.js'
];

self.addEventListener('install', event => {
  self.skipWaiting();
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then(cache => cache.addAll(urlsToCache))
  );
});

self.addEventListener('fetch', event => {
  event.respondWith(
    caches.match(event.request)
      .then(response => response || fetch(event.request))
      .catch(() => {
          // Fallback for offline if not cached
          if (event.request.mode === 'navigate') {
              return caches.match('/');
          }
      })
  );
});

self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys().then(cacheNames => {
            return Promise.all(
                cacheNames.map(cacheName => {
                    if (cacheName !== CACHE_NAME) {
                        return caches.delete(cacheName);
                    }
                })
            );
        })
    );
});
