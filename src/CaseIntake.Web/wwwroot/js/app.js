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

// Chart tooltips (Components/Kit/Charts/Chart.razor). Each point names its tooltip in data-tip; the tooltips are drawn
// in one layer after every point so nothing paints over them, which is why CSS :hover cannot show them. One tooltip
// shows at a time: on hover or keyboard focus of its point, and while the pointer is over the tooltip itself. Escape
// hides it until the pointer or focus moves to another point (WCAG 1.4.13). Listeners are on the document, so charts
// rendered later by Blazor need no setup.
let activeTip = null;     // { tip, by: 'pointer' | 'focus' }
let dismissedTip = null;

const tipFor = (target) => {
  if (!(target instanceof Element)) return null;
  const tip = target.closest('.chart__tip');
  if (tip) return tip;
  const point = target.closest('.chart__point[data-tip]');
  return point ? document.getElementById(point.dataset.tip) : null;
};

const hideTip = () => {
  activeTip?.tip.classList.remove('is-shown');
  activeTip = null;
};

const showTip = (tip, by) => {
  if (tip !== dismissedTip) dismissedTip = null;
  if (activeTip?.tip === tip) {
    activeTip.by = by;
    return;
  }
  hideTip();
  if (tip && tip !== dismissedTip) {
    tip.classList.add('is-shown');
    activeTip = { tip, by };
  }
};

document.addEventListener('pointerover', (event) => {
  const tip = tipFor(event.target);
  if (tip) {
    showTip(tip, 'pointer');
    return;
  }
  dismissedTip = null;   // the pointer left the point, so hovering it again shows the tooltip again
  if (activeTip?.by === 'pointer') hideTip();
});
document.addEventListener('pointerout', (event) => {
  if (event.relatedTarget === null && activeTip?.by === 'pointer') hideTip();   // left the window
});
document.addEventListener('focusin', (event) => {
  const tip = tipFor(event.target);
  if (tip) showTip(tip, 'focus');
});
document.addEventListener('focusout', (event) => {
  if (activeTip?.by === 'focus' && tipFor(event.target) === activeTip.tip) hideTip();
});
document.addEventListener('keydown', (event) => {
  if (event.key === 'Escape' && activeTip) {
    dismissedTip = activeTip.tip;
    hideTip();
  }
});

// Icon-button tooltips (CSS, [data-tooltip]): Escape hides any visible tooltip until the pointer moves or focus
// changes (WCAG 1.4.13 dismissible).
document.addEventListener('keydown', (event) => {
  if (event.key === 'Escape') document.documentElement.classList.add('tips-hidden');
});
const showTips = () => document.documentElement.classList.remove('tips-hidden');
document.addEventListener('pointermove', showTips, { passive: true });
document.addEventListener('focusin', showTips);

// Show / hide password (progressive enhancement; the sign-in and password pages are static server-rendered forms).
// The toggle is a real button that starts hidden and appears only when this script runs; it switches the field between
// password and text, and its pressed state and name say which. Pasting into the field is never blocked.
const enhancePasswordToggles = () => {
  document.querySelectorAll('button[data-password-toggle]:not([data-ready])').forEach((button) => {
    const input = document.getElementById(button.getAttribute('aria-controls'));
    if (!input) return;
    button.dataset.ready = 'true';
    button.hidden = false;
    const label = button.querySelector('.visually-hidden');
    const sync = () => {
      const shown = input.type === 'text';
      button.setAttribute('aria-pressed', shown ? 'true' : 'false');
      button.dataset.tooltip = shown ? 'Hide password' : 'Show password';
      if (label) label.textContent = 'Show password';
      button.querySelector('[data-eye]')?.toggleAttribute('hidden', shown);
      button.querySelector('[data-eye-off]')?.toggleAttribute('hidden', !shown);
    };
    button.addEventListener('click', () => {
      input.type = input.type === 'password' ? 'text' : 'password';
      sync();
      input.focus();
    });
    sync();
  });
};
enhancePasswordToggles();
document.addEventListener('DOMContentLoaded', enhancePasswordToggles);
// Blazor enhanced navigation swaps page content without a full load.
if (window.Blazor?.addEventListener) window.Blazor.addEventListener("enhancedload", enhancePasswordToggles);

// Donut and pie slices (Chart.razor): mouse users open a slice's case list by clicking it. The same links are in the
// chart's legend for keyboard and screen-reader users, so the slices are not links themselves.
document.addEventListener('click', (event) => {
  const slice = event.target instanceof Element ? event.target.closest('.chart__point--link[data-href]') : null;
  if (!slice) return;
  const href = slice.getAttribute('data-href');
  if (window.Blazor?.navigateTo) window.Blazor.navigateTo(href);
  else window.location.assign(href);
});

// Keep keyboard focus when a re-render replaces the focused control on the same page (WCAG 2.4.3). A filter, sort or page
// change is a query-string navigation; when the permission check behind it has to reload (its cache expires every
// minute), the router briefly shows "Checking your access" and rebuilds the page, which drops focus to <body>. When the
// control comes back (same id, or same data-testid) within a few seconds on the same path, focus returns to it. A move
// to another page is left to FocusOnNavigation (it focuses the new h1).
{
  let lost = null;
  let lastFocused = null;
  let lastPath = location.pathname;
  const keyOf = el => el.id ? `#${CSS.escape(el.id)}`
    : el.dataset?.testid ? `[data-testid="${CSS.escape(el.dataset.testid)}"]` : null;
  document.addEventListener('focusin', event => {
    if (event.target instanceof Element) {
      lastFocused = event.target;
      lastPath = location.pathname;
      lost = null;
    }
  }, true);
  new MutationObserver(() => {
    if (lost === null && lastFocused !== null && !lastFocused.isConnected) {
      const key = keyOf(lastFocused);
      lost = key === null ? null : { key, path: lastPath, until: performance.now() + 5000 };
      lastFocused = null;
    }
    if (lost === null) return;
    const focusIsLost = document.activeElement === null || document.activeElement === document.body;
    if (!focusIsLost || location.pathname !== lost.path || performance.now() > lost.until) {
      lost = null;
      return;
    }
    const replacement = document.querySelector(lost.key);
    if (replacement instanceof HTMLElement) {
      lost = null;
      replacement.focus();
    }
  }).observe(document.body, { childList: true, subtree: true });
}
