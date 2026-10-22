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

// Keep keyboard focus when a re-render replaces, removes or disables the focused control on the same page (WCAG 2.4.3).
// Browsers move focus to <body> in all three cases, so the next Tab starts again at the top of the page.
// - Replaced: a filter, sort or page change is a query-string navigation; when the permission check behind it has to
//   reload (its cache expires every minute), the router briefly shows "Checking your access" and rebuilds the page.
//   When the control comes back (same id, or a data-testid no other control has) within a few seconds, focus returns
//   to it.
// - Disabled while it works (Save, Refresh, Look up): focus returns to it when it is enabled again.
// - Removed or left disabled for good (Clear filters once nothing is filtered, a chip's remove button, Mark as read):
//   the control names where focus goes instead in data-focus-fallback, a list of selectors separated by "|" and tried
//   in order. A fallback that is not focusable (a heading) gets tabindex="-1".
// A move to another page is left to FocusOnNavigation (it focuses the new h1).
{
  let lost = null;
  let lastFocused = null;
  let lastPath = location.pathname;
  const keyOf = el => el.id ? `#${CSS.escape(el.id)}`
    : el.dataset?.testid ? `[data-testid="${CSS.escape(el.dataset.testid)}"]` : null;
  const usable = el => el instanceof HTMLElement && el.isConnected && !el.matches(':disabled');
  const fallbackOf = selectors => {
    for (const selector of (selectors ?? '').split('|').map(s => s.trim()).filter(Boolean)) {
      const el = document.querySelector(selector);
      if (usable(el)) {
        if (el.tabIndex < 0 && !el.hasAttribute('tabindex')) el.setAttribute('tabindex', '-1');
        return el;
      }
    }
    return null;
  };
  document.addEventListener('focusin', event => {
    if (event.target instanceof Element) {
      lastFocused = event.target;
      lastPath = location.pathname;
      lost = null;
    }
  }, true);
  const restore = () => {
    if (lost === null && lastFocused !== null) {
      const removed = !lastFocused.isConnected;
      if (removed || lastFocused.matches(':disabled')) {
        lost = {
          element: removed ? null : lastFocused,
          key: keyOf(lastFocused),
          fallback: lastFocused.getAttribute('data-focus-fallback'),
          path: lastPath,
          until: performance.now() + (removed ? 5000 : 15000),
        };
        lastFocused = null;
        if (!removed) {
          // Browsers move focus off a disabled control at their next rendering step, after this callback.
          requestAnimationFrame(() => setTimeout(restore));
        }
        if (lost.key === null && lost.fallback === null && lost.element === null) lost = null;
      }
    }
    if (lost === null) return;
    const active = document.activeElement;
    if (active !== null && active === lost.element) return;   // not moved yet (or the browser keeps it): wait
    const focusIsLost = active === null || active === document.body;
    if (!focusIsLost || location.pathname !== lost.path || performance.now() > lost.until) {
      lost = null;
      return;
    }
    // A rebuilt control with a unique key is the same control; a key shared by several rows (Mark as read) is not.
    const matches = lost.key === null ? [] : [...document.querySelectorAll(lost.key)].filter(usable);
    const target = usable(lost.element) ? lost.element
      : matches.length === 1 ? matches[0]
      : fallbackOf(lost.fallback) ?? matches[0] ?? null;
    if (target !== null) {
      lost = null;
      target.focus();
    }
  };
  new MutationObserver(restore).observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['disabled'] });
}

// DateInput's calendar button (the CSP forbids inline handlers): opens the native picker of the hidden date input
// just before it.
document.addEventListener('click', (event) => {
  const button = event.target instanceof Element ? event.target.closest('[data-date-picker]') : null;
  button?.previousElementSibling?.showPicker();
});
