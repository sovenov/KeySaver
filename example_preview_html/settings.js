(() => {
  const storageKey = 'keysaver.demo.settings.v1';
  const defaults = {
    mode: 'all',
    lists: { except: [], only: [] },
    rules: { click: true, tab: true, enter: false, idle: true },
    idleSeconds: 10,
  };
  const listTitles = { except: 'Исключённые приложения', only: 'Выбранные приложения' };
  const catalog = Object.entries(apps);
  const modes = document.querySelectorAll('input[name="capture-mode"]');
  const rules = document.querySelectorAll('.rule-switch');
  const idleSeconds = document.querySelector('#idle-seconds');
  const idleError = document.querySelector('#idle-error');
  const appPanel = document.querySelector('#scope-apps');
  const listTitle = document.querySelector('#scope-list-title');
  const list = document.querySelector('#scope-list');
  const empty = document.querySelector('#scope-list-empty');
  const form = document.querySelector('#scope-app-form');
  const choice = document.querySelector('#scope-app-choice');
  const customPath = document.querySelector('#scope-custom-path');
  const pathInput = document.querySelector('#scope-path-input');
  const addButton = document.querySelector('#scope-add-button');
  const appError = document.querySelector('#scope-app-error');

  const preferences = loadPreferences();

  function isValidSeconds(seconds) {
    return Number.isSafeInteger(seconds) && seconds >= 1;
  }

  function isExecutablePath(path) {
    return /^(?:[a-z]:\\|\\\\[^\\]+\\[^\\]+\\)(?:[^\\:*?<>|"\r\n]+\\)*[^\\:*?<>|"\r\n]+\.exe$/i.test(path);
  }

  function loadPreferences() {
    let stored;
    try {
      stored = JSON.parse(localStorage.getItem(storageKey));
    } catch {
      return defaults;
    }

    const mode = Array.from(modes).some(input => input.value === stored?.mode) ? stored.mode : defaults.mode;
    const lists = Object.fromEntries(Object.keys(defaults.lists).map(key => {
      const entries = stored?.lists?.[key];
      const valid = Array.isArray(entries) ? entries.filter(entry =>
        typeof entry?.name === 'string' && typeof entry?.path === 'string' && isExecutablePath(entry.path),
      ) : [];
      return [key, valid];
    }));
    const rules = Object.fromEntries(Object.entries(defaults.rules).map(([key, enabled]) => [
      key, typeof stored?.rules?.[key] === 'boolean' ? stored.rules[key] : enabled,
    ]));
    return { mode, lists, rules, idleSeconds: isValidSeconds(stored?.idleSeconds) ? stored.idleSeconds : defaults.idleSeconds };
  }

  function savePreferences() {
    try {
      localStorage.setItem(storageKey, JSON.stringify(preferences));
    } catch {
      // The design remains interactive in browsers with storage disabled.
    }
  }

  function setAppError(message = '') {
    appError.textContent = message;
    appError.hidden = !message;
  }

  function updateAppForm() {
    const manual = choice.value === 'custom';
    customPath.hidden = !manual;
    pathInput.disabled = !manual;
    addButton.disabled = !choice.value;
    pathInput.removeAttribute('aria-invalid');
    setAppError();
  }

  function hasPath(entries, path) {
    return entries.some(entry => entry.path.toLowerCase() === path.toLowerCase());
  }

  function renderApps() {
    const { mode, lists } = preferences;
    appPanel.hidden = mode === 'all';
    if (appPanel.hidden) return;

    const entries = lists[mode];
    listTitle.textContent = listTitles[mode];
    empty.hidden = entries.length > 0;
    empty.textContent = mode === 'except' ? 'Список исключений пуст.' : 'Добавьте приложения, для которых нужна запись.';
    for (const [key, app] of catalog) {
      choice.querySelector(`option[value="${key}"]`).disabled = hasPath(entries, app.path);
    }

    const rows = entries.map((entry, index) => {
      const row = document.createElement('li');
      row.innerHTML = '<div><strong></strong><code></code></div><button class="button" type="button">Удалить</button>';
      row.querySelector('strong').textContent = entry.name;
      row.querySelector('code').textContent = entry.path;
      const remove = row.querySelector('button');
      remove.setAttribute('aria-label', `Удалить ${entry.name}`);
      remove.addEventListener('click', () => {
        entries.splice(index, 1);
        savePreferences();
        renderApps();
        (list.children[index]?.querySelector('button') ?? choice).focus();
      });
      return row;
    });
    list.replaceChildren(...rows);
  }

  function addApp(event) {
    event.preventDefault();
    if (!choice.value || appPanel.hidden) return;

    const known = apps[choice.value];
    const path = known?.path ?? pathInput.value.trim().replace(/^"(.*)"$/, '$1').replaceAll('/', '\\');
    const entries = preferences.lists[preferences.mode];
    if (!isExecutablePath(path) || hasPath(entries, path)) {
      setAppError(!isExecutablePath(path) ? 'Укажите полный путь к файлу .exe.' : 'Это приложение уже есть в списке.');
      pathInput.setAttribute('aria-invalid', 'true');
      pathInput.focus();
      return;
    }

    entries.push({ name: known?.name ?? path.split('\\').at(-1), path });
    savePreferences();
    form.reset();
    updateAppForm();
    renderApps();
    choice.focus();
  }

  function updateIdleField() {
    idleSeconds.disabled = !preferences.rules.idle;
    const invalid = !idleSeconds.disabled && !isValidSeconds(idleSeconds.valueAsNumber);
    idleError.hidden = !invalid;
    idleSeconds.setAttribute('aria-invalid', invalid);
  }

  for (const [key, app] of catalog) {
    choice.add(new Option(app.name, key), choice.lastElementChild);
  }
  for (const radio of modes) {
    radio.checked = radio.value === preferences.mode;
    radio.addEventListener('change', () => {
      preferences.mode = radio.value;
      savePreferences();
      form.reset();
      updateAppForm();
      renderApps();
    });
  }
  for (const rule of rules) {
    const key = rule.dataset.rule;
    rule.checked = preferences.rules[key];
    rule.addEventListener('change', () => {
      preferences.rules[key] = rule.checked;
      if (key === 'idle' && !rule.checked) idleSeconds.value = preferences.idleSeconds;
      updateIdleField();
      savePreferences();
    });
  }
  idleSeconds.value = preferences.idleSeconds;
  idleSeconds.addEventListener('input', () => {
    updateIdleField();
    if (!idleError.hidden) return;
    preferences.idleSeconds = idleSeconds.valueAsNumber;
    savePreferences();
  });

  choice.addEventListener('change', () => {
    updateAppForm();
    if (!customPath.hidden) pathInput.focus();
  });
  pathInput.addEventListener('input', () => {
    pathInput.removeAttribute('aria-invalid');
    setAppError();
  });
  form.addEventListener('submit', addApp);
  updateAppForm();
  updateIdleField();
  renderApps();
})();
