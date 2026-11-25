const apps = {
  notepad: { name: 'Блокнот', icon: 'note', path: String.raw`C:\Windows\System32\notepad.exe` },
  code: {
    name: 'Visual Studio Code', icon: 'code',
    path: String.raw`C:\Users\Alex\AppData\Local\Programs\Microsoft VS Code\Code.exe`,
  },
  edge: {
    name: 'Microsoft Edge', icon: 'browser',
    path: String.raw`C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`,
  },
  outlook: {
    name: 'Outlook', icon: 'mail',
    path: String.raw`C:\Program Files\Microsoft Office\root\Office16\OUTLOOK.EXE`,
  },
};

const today = new Date();

function dayAtOffset(daysAgo) {
  return new Date(today.getFullYear(), today.getMonth(), today.getDate() - daysAgo);
}

function dayKey(date) {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

let sessions = [
  {
    app: 'notepad', daysAgo: 0, start: '16:42:05', seconds: 24,
    text: 'Заметки к встрече\n\n• Обновить список задач на неделю.\n• Подготовить черновик письма команде.\n• Сверить сроки по макету перед созвоном.',
  },
  {
    app: 'code', daysAgo: 0, start: '14:18:09', seconds: 19,
    text: 'const sessionTimeoutMs = 10_000;\nconst dateFormatter = new Intl.DateTimeFormat("ru-RU");',
  },
  {
    app: 'edge', daysAgo: 0, start: '11:06:12', seconds: 5,
    text: 'горячие клавиши для навигации по вкладкам Windows',
  },
  {
    app: 'outlook', daysAgo: 1, start: '17:31:04', seconds: 31,
    text: 'Коллеги, добрый день!\n\nПредлагаю обсудить макет в пятницу в 14:00. Если время подходит, я отправлю приглашение.',
  },
  {
    app: 'notepad', daysAgo: 1, start: '13:52:46', seconds: 26,
    text: 'Идеи для первого макета\n\nСписок сессий слева, полный текст справа. Сверху разместить поиск и два фильтра.',
  },
  {
    app: 'code', daysAgo: 1, start: '09:24:19', seconds: 12,
    text: '// Сгруппировать записи по локальной дате, сохранив время начала сессии.',
  },
  {
    app: 'edge', daysAgo: 2, start: '15:47:50', seconds: 9,
    text: 'сочетания клавиш для работы с текстом',
  },
  {
    app: 'notepad', daysAgo: 2, start: '10:12:06', seconds: 18,
    text: 'План на вечер\n\nЗабрать заказ, разобрать фотографии и выбрать книгу на выходные.',
  },
  {
    app: 'outlook', daysAgo: 8, start: '12:08:53', seconds: 22,
    text: 'Спасибо за материалы! Добавлю замечания к документу и пришлю его завтра утром.',
  },
].map((session, index) => {
  const day = dayKey(dayAtOffset(session.daysAgo));
  const start = new Date(`${day}T${session.start}`);
  return { ...session, id: index, day, start, end: new Date(start.getTime() + session.seconds * 1000) };
});

const dateFormat = new Intl.DateTimeFormat('ru-RU', { day: '2-digit', month: '2-digit', year: 'numeric' });
const dayFormat = new Intl.DateTimeFormat('ru-RU', { day: 'numeric', month: 'long' });
const shortTimeFormat = new Intl.DateTimeFormat('ru-RU', { hour: '2-digit', minute: '2-digit' });
const timeFormat = new Intl.DateTimeFormat('ru-RU', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
const plural = new Intl.PluralRules('ru-RU');

const historyView = document.querySelector('#history-view');
const historySidebar = document.querySelector('#history-sidebar');
const settingsView = document.querySelector('#settings-view');
const settingsButton = document.querySelector('#settings-button');
const settingsTabList = document.querySelector('.settings-tabs');
const settingsTabs = [...settingsTabList.querySelectorAll('[role="tab"]')];
const appSearch = document.querySelector('#app-search');
const appShortcuts = document.querySelector('#app-shortcuts');
const sidebarAppsEmpty = document.querySelector('#sidebar-apps-empty');
const searchInput = document.querySelector('#search-input');
const dateFilter = document.querySelector('#date-filter');
const customRange = document.querySelector('#custom-range');
const rangeError = document.querySelector('#range-error');
const dateStart = document.querySelector('#date-start');
const dateEnd = document.querySelector('#date-end');
const sessionCount = document.querySelector('#session-count');
const sessionList = document.querySelector('#session-list');
const emptyList = document.querySelector('#empty-list');
const emptyListTitle = document.querySelector('#empty-list-title');
const emptyListHint = document.querySelector('#empty-list-hint');
const resetFiltersButton = document.querySelector('#reset-filters');
const deleteButton = document.querySelector('#delete-sessions');
const deleteDialog = document.querySelector('#delete-dialog');
const deleteDialogTitle = document.querySelector('#delete-dialog-title');
const deleteDialogMessage = document.querySelector('#delete-dialog-message');
const sortButton = document.querySelector('#sort-button');
const detailPanel = document.querySelector('#detail-panel');
const detailContent = document.querySelector('#detail-content');
const emptyDetail = document.querySelector('#empty-detail');
const clearFilters = document.querySelector('#clear-filters');
const themeButton = document.querySelector('#theme-button');
const toast = document.querySelector('#toast');

let nextSessionId = sessions.length;
let filteredApp = 'all';
let visibleSessions = [];
let previewId = null;
let anchorId = sessions[0]?.id ?? null;
const selectedIds = new Set(anchorId === null ? [] : [anchorId]);
let oldestFirst = false;
let toastTimer;

function icon(name) {
  return `<svg class="icon" aria-hidden="true"><use href="#i-${name}"/></svg>`;
}

function appIcon(app) {
  const name = app === 'all' ? 'grid' : apps[app].icon;
  return `<span class="app-icon" data-app="${app}">${icon(name)}</span>`;
}

function countText(count) {
  const suffix = { one: 'сессия', few: 'сессии', many: 'сессий' };
  return `${count} ${suffix[plural.select(count)]}`;
}

function addAppShortcut(app, name) {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'app-shortcut';
  button.dataset.app = app;
  button.innerHTML = `${appIcon(app)}<span class="app-name"></span><span class="app-count"></span>`;
  button.querySelector('.app-name').textContent = name;
  button.addEventListener('click', () => {
    filteredApp = app;
    renderHistory();
  });
  appShortcuts.append(button);
}

addAppShortcut('all', 'Все приложения');
for (const [app, { name }] of Object.entries(apps)) {
  addAppShortcut(app, name);
}

function filterAppShortcuts() {
  const query = appSearch.value.trim().toLocaleLowerCase('ru-RU');
  let hasMatch = false;
  for (const shortcut of appShortcuts.children) {
    const app = shortcut.dataset.app;
    if (app === 'all') continue;
    const { name, path } = apps[app];
    const executable = path.split(/[\\/]/).at(-1);
    shortcut.hidden = !name.toLocaleLowerCase('ru-RU').includes(query)
      && !executable.toLocaleLowerCase('ru-RU').includes(query);
    hasMatch ||= !shortcut.hidden;
  }
  sidebarAppsEmpty.hidden = hasMatch;
  appShortcuts.scrollTop = 0;
}

function setSettingsOpen(open) {
  historyView.hidden = open;
  historySidebar.hidden = open;
  settingsView.hidden = !open;
  settingsButton.setAttribute('aria-expanded', open);
  const destination = open ? settingsTabList.querySelector('[aria-selected="true"]') : settingsButton;
  destination.focus();
}

function activateSettingsTab(activeTab) {
  for (const tab of settingsTabs) {
    const active = tab === activeTab;
    const panel = document.getElementById(tab.getAttribute('aria-controls'));
    panel.hidden = !active;
    tab.setAttribute('aria-selected', active);
    tab.tabIndex = active ? 0 : -1;
  }
  activeTab.focus();
}

function navigateSettingsTabs(event) {
  const index = settingsTabs.indexOf(event.target);
  if (index === -1) return;

  const last = settingsTabs.length - 1;
  let nextIndex;
  switch (event.key) {
    case 'ArrowLeft': nextIndex = index === 0 ? last : index - 1; break;
    case 'ArrowRight': nextIndex = index === last ? 0 : index + 1; break;
    case 'Home': nextIndex = 0; break;
    case 'End': nextIndex = last; break;
    default: return;
  }
  event.preventDefault();
  activateSettingsTab(settingsTabs[nextIndex]);
}

function dateBounds() {
  const todayKey = dayKey(today);
  const yesterdayKey = dayKey(dayAtOffset(1));
  switch (dateFilter.value) {
    case 'today': return [todayKey, todayKey];
    case 'yesterday': return [yesterdayKey, yesterdayKey];
    case 'week': return [dayKey(dayAtOffset(6)), todayKey];
    case 'range': return [dateStart.value, dateEnd.value];
    default: return ['', ''];
  }
}

function dayHeading(session) {
  const day = dayFormat.format(session.start);
  if (session.day === dayKey(today)) return `Сегодня, ${day}`;
  if (session.day === dayKey(dayAtOffset(1))) return `Вчера, ${day}`;
  return day[0].toLocaleUpperCase('ru-RU') + day.slice(1);
}

function exportSessionData() {
  return {
    sessions: sessions.map(session => ({
      app: session.app,
      startedAt: session.start.toISOString(),
      endedAt: session.end.toISOString(),
      text: session.text,
    })),
  };
}

function importSessionData(data) {
  if (data.sessions.some(session => !Object.hasOwn(apps, session.app))) {
    throw new Error('В файле есть приложение, которого нет в этой версии KeySaver.');
  }

  const key = ({ app, startedAt, endedAt, text }) => JSON.stringify([app, startedAt, endedAt, text]);
  const existing = exportSessionData().sessions;
  const known = new Set(existing.map(key));
  const incoming = [];

  for (const session of data.sessions) {
    const identity = key(session);
    if (known.has(identity)) continue;
    known.add(identity);
    incoming.push(session);
  }

  if (!incoming.length) return 0;
  transferFormat.stringifyJson({ sessions: existing.concat(incoming) });
  sessions = sessions.concat(incoming.map(session => {
    const start = new Date(session.startedAt);
    return { id: nextSessionId++, app: session.app, start, end: new Date(session.endedAt), day: dayKey(start), text: session.text };
  }));
  renderHistory();
  return incoming.length;
}

function sessionRow(session) {
  const row = document.createElement('button');
  const name = apps[session.app].name;
  const snippet = session.text.replace(/\s+/g, ' ');
  row.type = 'button';
  row.className = 'session-row';
  row.dataset.id = session.id;
  row.setAttribute('aria-controls', 'detail-panel');
  row.innerHTML = `${appIcon(session.app)}<span class="app-name"></span><time></time><span class="snippet"></span>`;
  row.querySelector('.app-name').textContent = name;
  row.querySelector('.snippet').textContent = snippet;
  const time = row.querySelector('time');
  time.dateTime = session.start.toISOString();
  time.textContent = shortTimeFormat.format(session.start);
  row.addEventListener('click', event => {
    const toggle = event.ctrlKey || event.metaKey;
    selectSession(session, { toggle, range: event.shiftKey });
    if (!toggle && !event.shiftKey && matchMedia('(max-width: 40rem)').matches) {
      detailPanel.focus({ preventScroll: true });
      detailPanel.scrollIntoView({ block: 'start' });
    }
  });
  const item = document.createElement('li');
  item.append(row);
  return item;
}

function renderHistory() {
  const app = filteredApp;
  const query = searchInput.value.trim().toLocaleLowerCase('ru-RU');
  const [from, to] = dateBounds();
  const reversedRange = Boolean(from && to && from > to);
  customRange.hidden = dateFilter.value !== 'range';
  rangeError.hidden = !reversedRange;
  const filtersActive = app !== 'all' || dateFilter.value !== 'all' || Boolean(searchInput.value);
  clearFilters.hidden = !filtersActive;

  visibleSessions = sessions.filter(session =>
    (app === 'all' || session.app === app) &&
    (!query || session.text.toLocaleLowerCase('ru-RU').includes(query)) &&
    (!from || session.day >= from) && (!to || session.day <= to),
  );
  visibleSessions.sort((a, b) => oldestFirst ? a.start - b.start : b.start - a.start);

  const visibleIds = new Set(visibleSessions.map(session => session.id));
  for (const id of selectedIds) {
    if (!visibleIds.has(id)) selectedIds.delete(id);
  }
  if (!visibleIds.has(anchorId)) anchorId = null;

  for (const shortcut of appShortcuts.children) {
    const shortcutApp = shortcut.dataset.app;
    const count = shortcutApp === 'all' ? sessions.length : sessions.filter(session => session.app === shortcutApp).length;
    shortcut.querySelector('.app-count').textContent = count;
    shortcut.setAttribute('aria-label', `${shortcut.querySelector('.app-name').textContent}, ${countText(count)}`);
    shortcut.setAttribute('aria-pressed', shortcutApp === app);
  }
  emptyList.hidden = visibleSessions.length > 0;
  sessionList.hidden = visibleSessions.length === 0;
  emptyListTitle.textContent = sessions.length ? 'Сессии не найдены' : 'Сессий пока нет';
  emptyListHint.hidden = !sessions.length;
  resetFiltersButton.hidden = !sessions.length || !filtersActive;

  const groups = new Map();
  for (const session of visibleSessions) {
    if (!groups.has(session.day)) {
      const group = document.createElement('section');
      const heading = document.createElement('h3');
      const rows = document.createElement('ul');
      group.className = 'day-group';
      heading.textContent = dayHeading(session);
      group.append(heading, rows);
      groups.set(session.day, { group, rows });
    }
    groups.get(session.day).rows.append(sessionRow(session));
  }
  sessionList.replaceChildren(...Array.from(groups.values(), ({ group }) => group));
  sessionList.scrollTop = 0;
  showSession(visibleSessions.find(session => session.id === previewId)
    ?? visibleSessions.find(session => selectedIds.has(session.id))
    ?? visibleSessions[0]);
  updateSelection();
}

function selectSession(session, { toggle, range }) {
  const anchorIndex = range ? visibleSessions.findIndex(item => item.id === anchorId) : -1;
  if (!toggle) selectedIds.clear();

  if (anchorIndex !== -1) {
    const index = visibleSessions.indexOf(session);
    const first = Math.min(index, anchorIndex);
    const last = Math.max(index, anchorIndex);
    for (const item of visibleSessions.slice(first, last + 1)) selectedIds.add(item.id);
  } else {
    if (selectedIds.has(session.id)) {
      selectedIds.delete(session.id);
    } else {
      selectedIds.add(session.id);
    }
    anchorId = session.id;
  }

  showSession(session);
  updateSelection();
}

function updateSelection() {
  const count = countText(visibleSessions.length);
  sessionCount.textContent = selectedIds.size > 1 ? `${count} · Выбрано ${selectedIds.size}` : count;
  deleteButton.disabled = selectedIds.size === 0;
  for (const row of sessionList.querySelectorAll('.session-row')) {
    const id = Number(row.dataset.id);
    row.setAttribute('aria-pressed', selectedIds.has(id));
    if (id === previewId) {
      row.setAttribute('aria-current', 'true');
    } else {
      row.removeAttribute('aria-current');
    }
  }
}

function deleteSelectedSessions() {
  const count = selectedIds.size;
  if (!count) return;

  const scrollTop = sessionList.scrollTop;
  if (selectedIds.has(previewId)) {
    const previewIndex = visibleSessions.findIndex(session => session.id === previewId);
    const next = visibleSessions.slice(previewIndex + 1).find(session => !selectedIds.has(session.id))
      ?? visibleSessions.findLast(session => !selectedIds.has(session.id));
    previewId = next?.id ?? null;
  }
  sessions = sessions.filter(session => !selectedIds.has(session.id));
  selectedIds.clear();
  anchorId = null;
  renderHistory();
  sessionList.scrollTop = scrollTop;
  const previewRow = sessionList.querySelector('.session-row[aria-current]');
  (previewRow ?? sortButton).focus();
  showToast(count === 1 ? 'Сессия удалена' : `Удалено ${countText(count)}`);
}

function showSession(session) {
  previewId = session?.id ?? null;
  detailContent.hidden = !session;
  emptyDetail.hidden = Boolean(session);
  detailPanel.setAttribute('aria-labelledby', session ? 'detail-title' : 'empty-detail-title');
  if (!session) {
    detailContent.replaceChildren();
    return;
  }

  const app = apps[session.app];
  detailContent.innerHTML = `
    <header class="detail-header">
      ${appIcon(session.app)}
      <div>
        <h2 class="detail-title" id="detail-title"></h2>
        <p class="detail-app-path"></p>
        <time data-field="date"></time>
      </div>
    </header>
    <div class="detail-body">
      <div class="text-heading">
        <h3>Сохранённый текст</h3>
        <button type="button" class="button copy-button">${icon('copy')}Копировать</button>
      </div>
      <pre class="saved-text"></pre>
      <dl class="session-meta">
        <div><dt>Начало</dt><dd><time data-field="start"></time></dd></div>
        <div><dt>Конец</dt><dd><time data-field="end"></time></dd></div>
      </dl>
    </div>`;

  detailContent.querySelector('#detail-title').textContent = app.name;
  detailContent.querySelector('.detail-app-path').textContent = app.path;
  detailContent.querySelector('.saved-text').textContent = session.text;
  setTime('date', session.start, `${dateFormat.format(session.start)} · ${timeFormat.format(session.start)}`);
  setTime('start', session.start, timeFormat.format(session.start));
  setTime('end', session.end, timeFormat.format(session.end));
  detailContent.querySelector('.copy-button').addEventListener('click', () => copyText(session.text));
}

function setTime(field, date, label) {
  const time = detailContent.querySelector(`[data-field="${field}"]`);
  time.dateTime = date.toISOString();
  time.textContent = label;
}

function showToast(message) {
  clearTimeout(toastTimer);
  toast.hidden = false;
  toast.textContent = message;
  toastTimer = setTimeout(() => { toast.hidden = true; }, 2800);
}

async function copyText(text) {
  const savedText = detailContent.querySelector('.saved-text');
  try {
    await navigator.clipboard.writeText(text);
    showToast('Текст скопирован');
  } catch {
    if (!savedText?.isConnected) return;
    const selection = getSelection();
    const range = document.createRange();
    range.selectNodeContents(savedText);
    selection.removeAllRanges();
    selection.addRange(range);
    showToast('Текст выделен. Нажмите Ctrl+C.');
  }
}

function resetFilters() {
  filteredApp = 'all';
  dateFilter.value = 'all';
  searchInput.value = '';
  dateStart.value = '';
  dateEnd.value = '';
  renderHistory();
  searchInput.focus();
}

function updateTheme(dark) {
  document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  const title = dark ? 'Включить светлую тему' : 'Включить тёмную тему';
  themeButton.innerHTML = icon(dark ? 'sun' : 'moon');
  themeButton.title = title;
  themeButton.setAttribute('aria-label', title);
}

for (const tab of settingsTabs) tab.addEventListener('click', () => activateSettingsTab(tab));
settingsTabList.addEventListener('keydown', navigateSettingsTabs);
settingsButton.addEventListener('click', () => setSettingsOpen(settingsView.hidden));
document.querySelector('#settings-back').addEventListener('click', () => setSettingsOpen(false));
appSearch.addEventListener('input', filterAppShortcuts);
searchInput.addEventListener('input', renderHistory);
dateFilter.addEventListener('change', renderHistory);
dateStart.addEventListener('input', renderHistory);
dateEnd.addEventListener('input', renderHistory);
clearFilters.addEventListener('click', resetFilters);
resetFiltersButton.addEventListener('click', resetFilters);
deleteButton.addEventListener('click', () => {
  const count = selectedIds.size;
  if (!count) return;

  deleteDialogTitle.textContent = count === 1 ? 'Удалить сессию?' : 'Удалить выбранные сессии?';
  deleteDialogMessage.textContent = count === 1
    ? 'Сессия будет удалена из истории.'
    : `Будут удалены выбранные сессии (${count}).`;
  deleteDialog.returnValue = '';
  deleteDialog.showModal();
  deleteDialog.querySelector('[autofocus]').focus();
});
deleteDialog.addEventListener('close', () => {
  if (deleteDialog.returnValue === 'yes') deleteSelectedSessions();
});
sortButton.addEventListener('click', () => {
  oldestFirst = !oldestFirst;
  sortButton.setAttribute('aria-pressed', oldestFirst);
  sortButton.title = oldestFirst ? 'Сначала старые' : 'Сначала новые';
  renderHistory();
});
themeButton.addEventListener('click', () => updateTheme(document.documentElement.dataset.theme !== 'dark'));
updateTheme(matchMedia('(prefers-color-scheme: dark)').matches);
filterAppShortcuts();
renderHistory();
