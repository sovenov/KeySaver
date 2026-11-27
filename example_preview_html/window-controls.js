(() => {
  const aboutDialog = document.querySelector('#about-dialog');
  const windowDialog = document.querySelector('#window-dialog');
  const windowError = document.querySelector('#window-error');
  let previewWindow;

  function centeredPosition(width, height) {
    const { availLeft = 0, availTop = 0, availWidth, availHeight } = window.screen;
    return {
      x: Math.round(availLeft + (availWidth - width) / 2),
      y: Math.round(availTop + (availHeight - height) / 2),
    };
  }

  function centerWindow() {
    if (!window.opener) {
      windowError.hidden = true;
      windowDialog.showModal();
      return;
    }

    const { x, y } = centeredPosition(window.outerWidth, window.outerHeight);
    window.moveTo(x, y);
    setTimeout(() => {
      if (Math.abs(window.screenX - x) > 2 || Math.abs(window.screenY - y) > 2) {
        showToast('Браузер ограничивает перемещение окна.');
      }
    }, 250);
  }

  function openPreviewWindow() {
    if (!previewWindow || previewWindow.closed) {
      const width = Math.min(window.innerWidth, window.screen.availWidth);
      const height = Math.min(window.innerHeight, window.screen.availHeight);
      const { x, y } = centeredPosition(width, height);
      previewWindow = window.open(window.location.href, 'keysaver-preview',
        `popup=yes,resizable=yes,width=${width},height=${height},left=${x},top=${y}`);
    }

    if (!previewWindow) {
      windowError.hidden = false;
      return;
    }
    windowDialog.close();
    previewWindow.focus();
  }

  document.querySelector('#about-button').addEventListener('click', () => aboutDialog.showModal());
  document.querySelector('#center-window').addEventListener('click', centerWindow);
  document.querySelector('#open-preview-window').addEventListener('click', openPreviewWindow);
})();
