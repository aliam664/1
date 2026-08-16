import path from 'node:path';

const WINDOWS_RESERVED = /^(con|prn|aux|nul|com[1-9]|lpt[1-9])(\.|$)/i;
const ILLEGAL_CHARS = /[<>:"|?*\u0000-\u001f]/;
const MAX_RELATIVE = 240;

/**
 * Normalize archive entry names to posix-style relative paths.
 * @param {string} entryName
 */
export function normalizeEntryName(entryName) {
  if (typeof entryName !== 'string' || entryName.length === 0) {
    throw unsafe('empty entry name');
  }
  if (entryName.includes('\0')) {
    throw unsafe('NUL byte in entry name');
  }
  return entryName.replace(/\\/g, '/').replace(/^\.?\//, '');
}

/**
 * Resolve `entryName` under `targetDir`. Throws if the entry is not a
 * safe relative path (zip-slip, absolute, reserved, symlink markers).
 *
 * @param {string} entryName
 * @param {string} targetDir
 * @returns {string} absolute destination path
 */
export function resolveSafeDestination(entryName, targetDir) {
  const unified = String(entryName).replace(/\\/g, '/');
  if (unified.startsWith('/') || /^[a-zA-Z]:/.test(unified) || unified.startsWith('//')) {
    throw unsafe(`absolute path rejected: ${entryName}`);
  }
  const normalized = normalizeEntryName(entryName);
  const parts = normalized.split('/').filter((part) => part && part !== '.');
  if (parts.some((part) => part === '..')) {
    throw unsafe(`path traversal rejected: ${entryName}`);
  }
  if (parts.some((part) => WINDOWS_RESERVED.test(part))) {
    throw unsafe(`reserved Windows name rejected: ${entryName}`);
  }
  if (parts.some((part) => ILLEGAL_CHARS.test(part) || part.endsWith('.') || part.endsWith(' '))) {
    throw unsafe(`illegal entry name rejected: ${entryName}`);
  }
  if (parts.some((part) => part.includes(':'))) {
    throw unsafe(`ADS / stream name rejected: ${entryName}`);
  }
  if (normalized.length > MAX_RELATIVE) {
    throw unsafe(`entry path too long: ${entryName}`);
  }

  const targetRoot = path.resolve(targetDir);
  const destination = path.resolve(targetRoot, ...parts);
  const rootWithSep = targetRoot.endsWith(path.sep) ? targetRoot : `${targetRoot}${path.sep}`;
  if (destination !== targetRoot && !destination.startsWith(rootWithSep)) {
    throw unsafe(`resolved path escaped target: ${entryName}`);
  }
  return destination;
}

/**
 * @param {string} reason
 */
function unsafe(reason) {
  const error = new Error(`Invalid or unsafe archive: ${reason}`);
  error.code = 'UNSAFE_ARCHIVE';
  return error;
}

export function isUnsafeArchiveError(error) {
  return Boolean(error && error.code === 'UNSAFE_ARCHIVE');
}

export { WINDOWS_RESERVED };
