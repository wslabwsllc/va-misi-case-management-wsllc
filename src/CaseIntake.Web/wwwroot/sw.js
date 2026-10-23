// Case Intake service worker. Explicit caching policy (ADR 0011):
//  - Cached: only static, non-sensitive application assets (CSS, JS, fonts, icons, the manifest, the offline page),
//    identified by path and file extension, same-origin, GET, successful basic responses.
//  - Never cached: page HTML (every page is rendered for the signed-in user), the Blazor circuit (/_blazor), sign-in and
//    sign-out (/account), attachment and test-evidence downloads, exports, health endpoints and anything else.
//  - Navigations always go to the network. Only when the network is unreachable does the worker answer with the static
//    offline page, which contains no user or case data.
const CACHE = 'caseintake-static-v2';   // v2: self-contained offline page (old caches are deleted on activate)
const OFFLINE_URL = '/offline.html';
const PRECACHE = [OFFLINE_URL, '/icons/icon.svg', '/icons/icon-192.png'];
const STATIC_EXTENSIONS = /\.(css|js|mjs|svg|png|ico|woff2?|webmanifest)$/i;
const NEVER = [/^\/_blazor/i, /^\/account\//i, /^\/cases\/\d+\/attachments\//i, /^\/test-results\//i, /^\/health/i, /^\/exports\//i];

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(CACHE).then((cache) => cache.addAll(PRECACHE)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((key) => key !== CACHE).map((key) => caches.delete(key))))
      .then(() => self.clients.claim()));
});

function isCacheableAsset(url) {
  return url.origin === self.location.origin
    && STATIC_EXTENSIONS.test(url.pathname)
    && !NEVER.some((pattern) => pattern.test(url.pathname));
}

self.addEventListener('fetch', (event) => {
  const request = event.request;
  if (request.method !== 'GET') {
    return;
  }

  if (request.mode === 'navigate') {
    event.respondWith(fetch(request).catch(() => caches.match(OFFLINE_URL)));
    return;
  }

  const url = new URL(request.url);
  if (!isCacheableAsset(url)) {
    return;   // Default browser handling, no worker caching.
  }

  // Network first so a deployment is picked up immediately; the cache is only a fallback when offline.
  event.respondWith(
    fetch(request)
      .then((response) => {
        if (response.ok && response.type === 'basic') {
          const copy = response.clone();
          // Best effort: a failed cache write (quota, storage cleared) must not fail the response.
          event.waitUntil(caches.open(CACHE).then((cache) => cache.put(request, copy)).catch(() => {}));
        }
        return response;
      })
      .catch(() => caches.match(request)));
});
