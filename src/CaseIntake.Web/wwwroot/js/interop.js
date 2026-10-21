// Application JS interop (E5). Imported once per circuit by UiInterop and disposed with it. Everything here is small,
// dependency-free DOM glue that Blazor cannot do from .NET: focusing by selector, native modal dialogs and image zoom.

const focusableSelector = 'a[href], button, input, select, textarea, [tabindex]';

/**
 * Focuses the first element matching the selector; headings get tabindex="-1" so they can take programmatic focus.
 * With waitMs, an element that is not on the page yet (for example while the router still shows "Checking your access")
 * is focused as soon as it appears, unless the user has moved focus somewhere else by then.
 */
export function focusSelector(selector, waitMs = 0) {
  const focus = element => {
    if (!element.matches(focusableSelector)) {
      element.setAttribute('tabindex', '-1');
    }
    element.focus();
  };
  const element = document.querySelector(selector);
  if (element) {
    focus(element);
    return;
  }
  if (waitMs <= 0) {
    return;
  }
  let timer = 0;
  const observer = new MutationObserver(() => {
    const found = document.querySelector(selector);
    if (!found) {
      return;
    }
    observer.disconnect();
    clearTimeout(timer);
    if (document.activeElement === null || document.activeElement === document.body) {
      focus(found);
    }
  });
  observer.observe(document.body, { childList: true, subtree: true });
  timer = setTimeout(() => observer.disconnect(), waitMs);
}

/**
 * Opens a native <dialog> with showModal(): the browser traps focus inside, makes the rest of the page inert and closes
 * on Esc. When the user dismisses it (Esc), .NET is told through OnDialogDismissed so it can return focus to the
 * trigger. A close requested by .NET (closeDialog) is not reported back.
 */
export function showDialog(dialog, callbacks) {
  if (!dialog || dialog.open) {
    return;
  }
  dialog.dataset.closedByApp = '';
  const onClose = () => {
    dialog.removeEventListener('close', onClose);
    if (dialog.dataset.closedByApp !== 'true') {
      callbacks.invokeMethodAsync('OnDialogDismissed').catch(() => { /* circuit gone */ });
    }
    dialog.dataset.closedByApp = '';
  };
  dialog.addEventListener('close', onClose);
  dialog.showModal();
  const first = dialog.querySelector('[autofocus]') || dialog.querySelector(focusableSelector);
  if (first) {
    first.focus();
  }
}

export function closeDialog(dialog) {
  if (!dialog || !dialog.open) {
    return;
  }
  dialog.dataset.closedByApp = 'true';
  dialog.close();
}

/** Zooms an evidence image by sizing it relative to its natural width (the viewer scrolls). */
export function setZoom(image, scale) {
  if (!image) {
    return;
  }
  const apply = () => {
    if (scale <= 0) {
      image.style.width = '';
      image.parentElement?.classList.add('evidence-viewer--fit');
    } else {
      image.parentElement?.classList.remove('evidence-viewer--fit');
      image.style.width = `${Math.round(image.naturalWidth * scale)}px`;
    }
  };
  if (image.complete) {
    apply();
  } else {
    image.addEventListener('load', apply, { once: true });
  }
}
