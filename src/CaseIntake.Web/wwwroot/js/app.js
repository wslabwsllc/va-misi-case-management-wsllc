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
