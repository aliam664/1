/**
 * Minimal VDF parser for Steam `libraryfolders.vdf`.
 * Only string keys/values and nested objects are supported.
 *
 * @param {string} text
 * @returns {Record<string, unknown>}
 */
export function parseVdf(text) {
  const tokens = tokenize(text);
  let index = 0;

  function parseValue() {
    const token = tokens[index];
    if (token === '{') {
      index += 1;
      /** @type {Record<string, unknown>} */
      const obj = {};
      while (tokens[index] !== '}' && index < tokens.length) {
        const key = tokens[index];
        index += 1;
        obj[key] = parseValue();
      }
      if (tokens[index] === '}') {
        index += 1;
      }
      return obj;
    }
    index += 1;
    return token;
  }

  /** @type {Record<string, unknown>} */
  const root = {};
  while (index < tokens.length) {
    const key = tokens[index];
    index += 1;
    root[key] = parseValue();
  }
  return root;
}

function tokenize(text) {
  /** @type {string[]} */
  const tokens = [];
  let i = 0;
  while (i < text.length) {
    const ch = text[i];
    if (/\s/.test(ch)) {
      i += 1;
      continue;
    }
    if (ch === '/' && text[i + 1] === '/') {
      while (i < text.length && text[i] !== '\n') {
        i += 1;
      }
      continue;
    }
    if (ch === '{' || ch === '}') {
      tokens.push(ch);
      i += 1;
      continue;
    }
    if (ch === '"') {
      i += 1;
      let value = '';
      while (i < text.length && text[i] !== '"') {
        if (text[i] === '\\' && i + 1 < text.length) {
          i += 1;
        }
        value += text[i];
        i += 1;
      }
      i += 1;
      tokens.push(value);
      continue;
    }
    let bare = '';
    while (i < text.length && !/\s/.test(text[i]) && text[i] !== '{' && text[i] !== '}') {
      bare += text[i];
      i += 1;
    }
    if (bare) {
      tokens.push(bare);
    }
  }
  return tokens;
}

/**
 * @param {Record<string, unknown>} parsed
 * @returns {string[]}
 */
export function extractSteamLibraries(parsed) {
  const folders = parsed.libraryfolders || parsed.LibraryFolders || parsed;
  if (!folders || typeof folders !== 'object') {
    return [];
  }
  const paths = [];
  for (const value of Object.values(/** @type {Record<string, unknown>} */ (folders))) {
    if (typeof value === 'string' && value.includes(':')) {
      paths.push(value);
    } else if (value && typeof value === 'object' && typeof value.path === 'string') {
      paths.push(value.path);
    }
  }
  return paths;
}
