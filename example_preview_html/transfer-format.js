const transferFormat = (() => {
  const jsonFormat = 'keysaver-json';
  const archiveFormat = 'keysaver-archive';
  const version = 1;
  const maxFileBytes = 16 * 1024 * 1024;
  const maxDataBytes = 10 * 1024 * 1024;
  const maxSessions = 5_000;
  const maxPasswordBytes = 1_024;
  const iterations = 600_000;
  const saltLength = 16;
  const ivLength = 12;
  const tagLength = 128;
  const encoder = new TextEncoder();
  const decoder = new TextDecoder('utf-8', { fatal: true });
  const additionalData = encoder.encode(`${archiveFormat}:${version}:encrypted`);
  const timestampFormat = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?(?:Z|[+-]\d{2}:\d{2})$/;

  function normalizeTimestamp(value) {
    if (typeof value !== 'string' || !timestampFormat.test(value)) return null;
    const wallTime = value.slice(0, 19);
    const calendarDate = new Date(`${wallTime}Z`);
    const absoluteDate = new Date(value);
    if (!Number.isFinite(calendarDate.getTime()) || !Number.isFinite(absoluteDate.getTime())) return null;
    if (calendarDate.toISOString().slice(0, 19) !== wallTime) return null;
    const normalized = absoluteDate.toISOString();
    return normalized.length === 24 ? normalized : null;
  }

  function normalizeData(data) {
    if (!Array.isArray(data?.sessions)) throw new Error('В файле отсутствует список сессий.');
    if (data.sessions.length > maxSessions) throw new Error('Файл содержит больше 5 000 сессий.');

    const sessions = data.sessions.map((session, index) => {
      const record = `сессии №${index + 1}`;
      if (typeof session?.app !== 'string' || !session.app.trim() || session.app.length > 128) {
        throw new Error(`Укажите приложение для ${record}.`);
      }
      const startedAt = normalizeTimestamp(session.startedAt);
      const endedAt = normalizeTimestamp(session.endedAt);
      if (!startedAt || !endedAt || endedAt < startedAt) {
        throw new Error(`Проверьте время начала и окончания ${record}.`);
      }
      if (typeof session.text !== 'string') throw new Error(`Проверьте текст ${record}.`);
      return { app: session.app, startedAt, endedAt, text: session.text };
    });
    return { sessions };
  }

  function prepareData(input) {
    const data = normalizeData(input);
    const bytes = encoder.encode(JSON.stringify(data));
    if (bytes.length > maxDataBytes) throw new Error('Объём истории превышает 10 МБ.');
    return { data, bytes };
  }

  function checkFileSize(source) {
    if (source.length > maxFileBytes || encoder.encode(source).length > maxFileBytes) {
      throw new Error('Размер файла превышает 16 МБ.');
    }
  }

  function stringifyJson(input) {
    const { data } = prepareData(input);
    const source = JSON.stringify({ format: jsonFormat, version, data }, null, 2);
    checkFileSize(source);
    return source;
  }

  function encodeBase64(bytes) {
    let binary = '';
    const chunkSize = 32_768;
    for (let offset = 0; offset < bytes.length; offset += chunkSize) {
      binary += String.fromCharCode(...bytes.subarray(offset, offset + chunkSize));
    }
    return btoa(binary);
  }

  function decodeBase64(value, minLength, maxLength = minLength) {
    const invalid = () => new Error('Архив повреждён. Выберите другой файл.');
    if (typeof value !== 'string' || value.length > Math.ceil(maxLength / 3) * 4
      || value.length % 4 || /[^A-Za-z0-9+/=]/.test(value)) {
      throw invalid();
    }
    let binary;
    try {
      binary = atob(value);
    } catch {
      throw invalid();
    }
    if (binary.length < minLength || binary.length > maxLength) throw invalid();
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
    return bytes;
  }

  function cryptoProvider() {
    if (!globalThis.crypto?.subtle) {
      throw new Error('Для работы с паролем откройте KeySaver на локальном адресе или через HTTPS.');
    }
    return globalThis.crypto;
  }

  async function deriveKey(crypto, password, salt, usage) {
    if (!password) throw new Error('Введите пароль архива.');
    if (password.length > maxPasswordBytes) throw new Error('Пароль слишком длинный.');
    const bytes = encoder.encode(password);
    let material;
    try {
      if (bytes.length > maxPasswordBytes) throw new Error('Пароль слишком длинный.');
      material = await crypto.subtle.importKey('raw', bytes, 'PBKDF2', false, ['deriveKey']);
    } finally {
      bytes.fill(0);
    }
    return crypto.subtle.deriveKey(
      { name: 'PBKDF2', hash: 'SHA-256', salt, iterations },
      material,
      { name: 'AES-GCM', length: 256 },
      false,
      [usage],
    );
  }

  function cipherParams(iv) {
    return { name: 'AES-GCM', iv, additionalData, tagLength };
  }

  async function stringifyArchive(input, { password = '' } = {}) {
    const { data, bytes } = prepareData(input);
    if (!password) return JSON.stringify({ format: archiveFormat, version, mode: 'plain', data });

    const crypto = cryptoProvider();
    const salt = crypto.getRandomValues(new Uint8Array(saltLength));
    const iv = crypto.getRandomValues(new Uint8Array(ivLength));
    const key = await deriveKey(crypto, password, salt, 'encrypt');
    const content = await crypto.subtle.encrypt(cipherParams(iv), key, bytes);
    return JSON.stringify({
      format: archiveFormat,
      version,
      mode: 'encrypted',
      salt: encodeBase64(salt),
      iv: encodeBase64(iv),
      content: encodeBase64(new Uint8Array(content)),
    });
  }

  function read(source) {
    checkFileSize(source);
    let file;
    try {
      file = JSON.parse(source.replace(/^\uFEFF/, ''));
    } catch {
      throw new Error('Файл повреждён. Проверьте, что выбрали файл экспорта KeySaver.');
    }

    if (file?.format !== jsonFormat && file?.format !== archiveFormat) {
      throw new Error('Выберите JSON или архив, экспортированный из KeySaver.');
    }
    if (file.version !== version) throw new Error('Эта версия формата KeySaver пока не поддерживается.');

    if (file.format === jsonFormat) {
      return { kind: 'json', requiresPassword: false, data: prepareData(file.data).data };
    }
    if (file.mode === 'plain') {
      return { kind: 'archive', requiresPassword: false, data: prepareData(file.data).data };
    }
    if (file.mode !== 'encrypted') throw new Error('Архив повреждён. Выберите другой файл.');

    return {
      kind: 'archive',
      requiresPassword: true,
      salt: decodeBase64(file.salt, saltLength),
      iv: decodeBase64(file.iv, ivLength),
      content: decodeBase64(file.content, tagLength / 8, maxDataBytes + tagLength / 8),
    };
  }

  async function decode(file, { password = '' } = {}) {
    if (!file.requiresPassword) return file.data;
    const crypto = cryptoProvider();
    const key = await deriveKey(crypto, password, file.salt, 'decrypt');
    let bytes;
    try {
      bytes = await crypto.subtle.decrypt(cipherParams(file.iv), key, file.content);
    } catch {
      throw new Error('Не удалось открыть архив. Проверьте пароль и целостность файла.');
    }
    let data;
    try {
      data = JSON.parse(decoder.decode(bytes));
    } catch {
      throw new Error('Данные внутри архива повреждены.');
    }
    return normalizeData(data);
  }

  return { maxFileBytes, stringifyJson, stringifyArchive, read, decode };
})();
