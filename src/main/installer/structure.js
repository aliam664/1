import { existsSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';

/**
 * @typedef {object} DetectedItem
 * @property {'car' | 'track' | 'other'} kind
 * @property {string} folderName
 * @property {string} sourceDir
 * @property {string} relativeSource
 * @property {string} suggestedDest
 */

/**
 * Walk `rootDir` and return every file as a posix-relative path.
 * @param {string} rootDir
 * @returns {string[]}
 */
export function listRelativeFiles(rootDir) {
  /** @type {string[]} */
  const files = [];
  function walk(dir, rel) {
    let entries = [];
    try {
      entries = readdirSync(dir, { withFileTypes: true });
    } catch {
      return;
    }
    for (const entry of entries) {
      const nextRel = rel ? `${rel}/${entry.name}` : entry.name;
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        walk(full, nextRel);
      } else if (entry.isFile()) {
        files.push(nextRel.replace(/\\/g, '/'));
      }
    }
  }
  walk(rootDir, '');
  return files.sort();
}

function hasFile(files, predicate) {
  return files.some(predicate);
}

/**
 * @param {string[]} files relative posix paths
 * @param {string} extractRoot
 */
export function analyzeExtractedTree(files, extractRoot) {
  const normalized = files.map((f) => f.replace(/\\/g, '/'));
  /** @type {DetectedItem[]} */
  const items = [];
  /** @type {{ path: string, kind: string }[]} */
  const extras = [];

  const contentCar = new Set();
  const contentTrack = new Set();
  for (const file of normalized) {
    const carMatch = file.match(/(?:^|\/)content\/cars\/([^/]+)\//);
    if (carMatch) {
      contentCar.add(carMatch[1]);
    }
    const trackMatch = file.match(/(?:^|\/)content\/tracks\/([^/]+)\//);
    if (trackMatch) {
      contentTrack.add(trackMatch[1]);
    }
    if (/(?:^|\/)(apps|extension|system)\//.test(file) || /\.(pdf|txt|md|jpg|png|webp)$/i.test(file)) {
      extras.push({ path: file, kind: classifyExtra(file) });
    }
  }

  if (contentCar.size || contentTrack.size) {
    for (const name of contentCar) {
      const sourceDir = findDir(extractRoot, ['content', 'cars', name]);
      items.push({
        kind: 'car',
        folderName: name,
        sourceDir,
        relativeSource: relFrom(extractRoot, sourceDir),
        suggestedDest: `content/cars/${name}`
      });
    }
    for (const name of contentTrack) {
      const sourceDir = findDir(extractRoot, ['content', 'tracks', name]);
      items.push({
        kind: 'track',
        folderName: name,
        sourceDir,
        relativeSource: relFrom(extractRoot, sourceDir),
        suggestedDest: `content/tracks/${name}`
      });
    }
    return finalize(items, extras, extractRoot);
  }

  const folders = uniqueFolders(normalized);
  for (const folder of folders) {
    const folderFiles = normalized
      .filter((file) => file === folder || file.startsWith(`${folder}/`))
      .map((file) => (file === folder ? path.posix.basename(file) : file.slice(folder.length + 1)));
    const kind = classifyFolder(folderFiles);
    if (kind === 'car' || kind === 'track') {
      const folderName = unwrapNestedName(folder, folderFiles, kind);
      items.push({
        kind,
        folderName,
        sourceDir: path.join(extractRoot, ...folder.split('/')),
        relativeSource: folder,
        suggestedDest: kind === 'car' ? `content/cars/${folderName}` : `content/tracks/${folderName}`
      });
    }
  }

  // Root itself might be a single car/track.
  if (items.length === 0) {
    const kind = classifyFolder(normalized);
    if (kind === 'car' || kind === 'track') {
      const folderName = path.basename(extractRoot);
      items.push({
        kind,
        folderName,
        sourceDir: extractRoot,
        relativeSource: '',
        suggestedDest: kind === 'car' ? `content/cars/${folderName}` : `content/tracks/${folderName}`
      });
    }
  }

  return finalize(items, extras, extractRoot);
}

function classifyFolder(files) {
  if (
    hasFile(files, (f) => /(^|\/)data\.acd$/i.test(f)) ||
    hasFile(files, (f) => /(^|\/)ui\/ui_car\.json$/i.test(f)) ||
    hasFile(files, (f) => /(^|\/)sfx\//i.test(f))
  ) {
    return 'car';
  }
  if (
    hasFile(files, (f) => /(^|\/)models\.ini$/i.test(f)) ||
    hasFile(files, (f) => /(^|\/)surfaces\.ini$/i.test(f)) ||
    (hasFile(files, (f) => /(^|\/)data\//i.test(f)) && hasFile(files, (f) => /(^|\/)ui\//i.test(f)))
  ) {
    return 'track';
  }
  return 'other';
}

function unwrapNestedName(folder, folderFiles, kind) {
  const base = folder.split('/').filter(Boolean).pop() || folder;
  const nested = `${base}/${base}/`;
  const marker = kind === 'car' ? 'data.acd' : 'models.ini';
  if (folderFiles.some((file) => file.toLowerCase() === `${base}/${marker}` || file.toLowerCase() === `${nested}${marker}`)) {
    return base;
  }
  return base;
}

function uniqueFolders(files) {
  const set = new Set();
  for (const file of files) {
    const parts = file.split('/');
    if (parts.length > 1) {
      set.add(parts[0]);
    }
  }
  return [...set];
}

function classifyExtra(file) {
  if (/(?:^|\/)apps\//.test(file)) {
    return 'app';
  }
  if (/(?:^|\/)extension\//.test(file)) {
    return 'extension';
  }
  if (/(?:^|\/)system\//.test(file)) {
    return 'system';
  }
  return 'document';
}

function findDir(root, segments) {
  const direct = path.join(root, ...segments);
  if (existsSync(direct)) {
    return direct;
  }
  const stack = [root];
  while (stack.length) {
    const current = stack.pop();
    if (!current) {
      break;
    }
    let entries = [];
    try {
      entries = readdirSync(current, { withFileTypes: true });
    } catch {
      continue;
    }
    for (const entry of entries) {
      if (!entry.isDirectory()) {
        continue;
      }
      const full = path.join(current, entry.name);
      const candidate = path.join(full, ...segments);
      if (existsSync(candidate) && statSync(candidate).isDirectory()) {
        return candidate;
      }
      stack.push(full);
    }
  }
  return path.join(root, ...segments);
}

function relFrom(root, full) {
  return path.relative(root, full).split(path.sep).join('/');
}

function finalize(items, extras, extractRoot) {
  const unique = [];
  const seen = new Set();
  for (const item of items) {
    const key = `${item.kind}:${item.folderName}`;
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);
    unique.push(item);
  }
  return {
    extractRoot,
    items: unique,
    extras: extras.filter((extra) => !unique.some((item) => extra.path.startsWith(`${item.relativeSource}/`))),
    recognized: unique.length > 0
  };
}
