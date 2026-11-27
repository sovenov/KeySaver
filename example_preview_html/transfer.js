(() => {
  const exportForm = document.querySelector('#export-form');
  const exportControls = exportForm.querySelector('fieldset');
  const exportFormat = document.querySelector('#export-format');
  const jsonNote = document.querySelector('#export-json-note');
  const archiveSettings = document.querySelector('#archive-settings');
  const protectArchive = document.querySelector('#export-protect');
  const exportPasswords = document.querySelector('#export-passwords');
  const exportPassword = document.querySelector('#export-password');
  const exportRepeat = document.querySelector('#export-repeat');
  const exportStatus = document.querySelector('#export-status');

  const importForm = document.querySelector('#import-form');
  const importControls = importForm.querySelector('fieldset');
  const importFile = document.querySelector('#import-file');
  const importFileNote = document.querySelector('#import-file-note');
  const importPasswordField = document.querySelector('#import-password-field');
  const importPassword = document.querySelector('#import-password');
  const importSubmit = document.querySelector('#import-submit');
  const importStatus = document.querySelector('#import-status');
  const initialFileNote = importFileNote.textContent;
  let documentToImport = null;
  let fileReadId = 0;

  function setStatus(output, message = '', error = false) {
    output.textContent = message;
    output.toggleAttribute('data-error', error);
    output.hidden = !message;
  }

  function updateExportOptions() {
    const archive = exportFormat.value === 'archive';
    const passwordFields = archive && protectArchive.checked;
    jsonNote.hidden = archive;
    archiveSettings.hidden = !archive;
    protectArchive.disabled = !archive;
    exportPasswords.hidden = !passwordFields;
    exportPassword.disabled = !passwordFields;
    exportRepeat.disabled = !passwordFields;
    if (!passwordFields) {
      exportPassword.value = '';
      exportRepeat.value = '';
    }
    setStatus(exportStatus);
  }

  function downloadHistory(source, extension) {
    const file = new Blob([source], { type: 'application/json;charset=utf-8' });
    const url = URL.createObjectURL(file);
    const link = document.createElement('a');
    link.href = url;
    link.download = `KeySaver-${dayKey(new Date())}.${extension}`;
    document.body.append(link);
    try {
      link.click();
    } finally {
      link.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
  }

  async function exportHistory(event) {
    event.preventDefault();
    if (exportControls.disabled) return;

    const archive = exportFormat.value === 'archive';
    const password = archive && protectArchive.checked ? exportPassword.value : '';
    if (password && password !== exportRepeat.value) {
      setStatus(exportStatus, 'Пароли не совпадают.', true);
      exportRepeat.focus();
      return;
    }

    exportControls.disabled = true;
    setStatus(exportStatus, 'Подготовка файла…');
    try {
      const data = exportSessionData();
      const source = archive ? await transferFormat.stringifyArchive(data, { password }) : transferFormat.stringifyJson(data);
      downloadHistory(source, archive ? 'keysaver' : 'json');
      exportPassword.value = '';
      exportRepeat.value = '';
      setStatus(exportStatus, 'Файл истории подготовлен к скачиванию.');
    } catch (error) {
      setStatus(exportStatus, error.message, true);
    } finally {
      exportControls.disabled = false;
    }
  }

  async function readImportFile() {
    const readId = ++fileReadId;
    const file = importFile.files[0];
    documentToImport = null;
    importFileNote.textContent = initialFileNote;
    importPassword.value = '';
    importPassword.disabled = true;
    importPasswordField.hidden = true;
    importSubmit.disabled = true;
    setStatus(importStatus);
    if (!file) return;

    try {
      if (file.size > transferFormat.maxFileBytes) throw new Error('Размер файла превышает 16 МБ.');
      const source = await file.text();
      if (readId !== fileReadId) return;

      documentToImport = transferFormat.read(source);
      const protectedArchive = documentToImport.requiresPassword;
      importPasswordField.hidden = !protectedArchive;
      importPassword.disabled = !protectedArchive;
      importSubmit.disabled = false;
      importFileNote.textContent = protectedArchive ? 'Архив KeySaver с паролем.'
        : documentToImport.kind === 'json' ? 'Файл JSON.' : 'Архив KeySaver без пароля.';
    } catch (error) {
      if (readId === fileReadId) setStatus(importStatus, error.message || 'Не удалось прочитать файл.', true);
    }
  }

  async function importHistory(event) {
    event.preventDefault();
    if (!documentToImport || importControls.disabled) return;

    importControls.disabled = true;
    setStatus(importStatus, 'Импорт истории…');
    try {
      const data = await transferFormat.decode(documentToImport, { password: importPassword.value });
      const added = importSessionData(data);
      importPassword.value = '';
      if (added === 1) {
        setStatus(importStatus, 'Добавлена 1 сессия.');
      } else if (added > 1) {
        setStatus(importStatus, `Добавлено ${countText(added)}.`);
      } else {
        setStatus(importStatus, data.sessions.length ? 'Все сессии из файла уже есть в истории.' : 'В файле нет сессий.');
      }
    } catch (error) {
      setStatus(importStatus, error.message, true);
    } finally {
      importControls.disabled = false;
    }
  }

  exportFormat.addEventListener('change', updateExportOptions);
  protectArchive.addEventListener('change', updateExportOptions);
  for (const field of [exportPassword, exportRepeat]) {
    field.addEventListener('input', () => {
      if (exportStatus.hasAttribute('data-error')) setStatus(exportStatus);
    });
  }
  exportForm.addEventListener('submit', exportHistory);
  importFile.addEventListener('change', readImportFile);
  importForm.addEventListener('submit', importHistory);
  updateExportOptions();
})();
