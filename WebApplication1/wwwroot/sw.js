// HomeNest Service Worker for PWA & Push Notifications
self.addEventListener('install', event => {
    self.skipWaiting();
});

self.addEventListener('activate', event => {
    event.waitUntil(self.clients.claim());
});

self.addEventListener('fetch', event => {
    // Cache-first or network-first strategy
    event.respondWith(fetch(event.request).catch(() => caches.match(event.request)));
});

self.addEventListener('push', event => {
    const data = event.data ? event.data.json() : {};
    const title = data.title || 'HomeNest Notification';
    const options = {
        body: data.body || 'You have an upcoming check-in reminder!',
        icon: '/favicon.ico',
        badge: '/favicon.ico'
    };
    event.waitUntil(self.registration.showNotification(title, options));
});

