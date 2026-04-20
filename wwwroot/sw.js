// FV-CORE Passive Service Worker for PWA Installation Support
// IMPORTANT: This worker uses a passthrough strategy to avoid breaking AJAX navigation.

self.addEventListener('install', (event) => {
    self.skipWaiting();
});

self.addEventListener('activate', (event) => {
    return self.clients.claim();
});

// Passthrough fetch event (NO CACHING)
self.addEventListener('fetch', (event) => {
    // We intentionally do nothing here to let the browser handle requests normally.
    // This satisfies the PWA requirement of having a fetch handler.
});
