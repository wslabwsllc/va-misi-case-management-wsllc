// Page-level script (loaded once by App.razor after blazor.web.js). Registers the service worker that makes the app
// installable and shows an offline page. The worker never caches pages, case data, downloads or sign-in responses
// (see sw.js and ADR 0011). Browsers only allow service workers on HTTPS or localhost; elsewhere this is skipped and
// the app works normally in the browser.
if ('serviceWorker' in navigator && window.isSecureContext) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js', { scope: '/' }).catch(() => {
      // Installation is optional; the application does not depend on the worker.
    });
  });
}
