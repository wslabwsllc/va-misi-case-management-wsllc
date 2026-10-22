// Drives the reconnect dialog from Blazor's reconnection events (.NET 10 template behaviour, ADR 0010).
const reconnectModal = document.getElementById('components-reconnect-modal');
reconnectModal.addEventListener('components-reconnect-state-changed', handleReconnectStateChanged);
// Esc would close the dialog while the connection is still lost, hiding the later Retry, Resume and Reload actions.
reconnectModal.addEventListener('cancel', event => event.preventDefault());

const retryButton = document.getElementById('components-reconnect-button');
retryButton.addEventListener('click', retry);

const resumeButton = document.getElementById('components-resume-button');
resumeButton.addEventListener('click', resume);

function handleReconnectStateChanged(event) {
  if (event.detail.state === 'show') {
    if (!reconnectModal.open) {
      reconnectModal.showModal();
    }
  } else if (event.detail.state === 'hide') {
    reconnectModal.close();
  } else if (event.detail.state === 'failed') {
    document.addEventListener('visibilitychange', retryWhenDocumentBecomesVisible);
  } else if (event.detail.state === 'rejected') {
    location.reload();
  }
  requestAnimationFrame(focusVisibleAction);
}

// Each state shows its own action and hides the others, so the button just pressed (Retry, Resume) can disappear and
// drop focus to <body> (WCAG 2.4.3). Move focus to the action now shown, or to the dialog's heading.
function focusVisibleAction() {
  if (!reconnectModal.open) return;
  const isVisible = el => el.getClientRects().length > 0;
  const active = document.activeElement;
  const focusIsLost = active === null || active === document.body
    || (reconnectModal.contains(active) && active !== reconnectModal && !isVisible(active));
  if (!focusIsLost) return;
  const action = [...reconnectModal.querySelectorAll('button, a[href]')].find(isVisible);
  (action ?? document.getElementById('components-reconnect-title')).focus();
}

async function retry() {
  document.removeEventListener('visibilitychange', retryWhenDocumentBecomesVisible);
  try {
    // Reconnect returns false when the server no longer has the circuit; then try to resume it, else reload.
    const successful = await Blazor.reconnect();
    if (!successful) {
      const resumeSuccessful = await Blazor.resumeCircuit();
      if (!resumeSuccessful) {
        location.reload();
      } else {
        reconnectModal.close();
      }
    }
  } catch (err) {
    // The server is still unreachable: try again when the user returns to the tab.
    document.addEventListener('visibilitychange', retryWhenDocumentBecomesVisible);
  }
}

async function resume() {
  try {
    const successful = await Blazor.resumeCircuit();
    if (!successful) {
      location.reload();
    }
  } catch {
    reconnectModal.classList.replace('components-reconnect-paused', 'components-reconnect-resume-failed');
    requestAnimationFrame(focusVisibleAction);
  }
}

async function retryWhenDocumentBecomesVisible() {
  if (document.visibilityState === 'visible') {
    await retry();
  }
}
