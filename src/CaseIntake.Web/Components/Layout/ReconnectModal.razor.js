// Drives the reconnect dialog from Blazor's reconnection events (.NET 10 template behaviour, ADR 0010).
const reconnectModal = document.getElementById('components-reconnect-modal');
reconnectModal.addEventListener('components-reconnect-state-changed', handleReconnectStateChanged);

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
  }
}

async function retryWhenDocumentBecomesVisible() {
  if (document.visibilityState === 'visible') {
    await retry();
  }
}
