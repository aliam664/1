import { basename } from 'node:path';

export const MAGIC = Object.freeze({
  zip: Buffer.from([0x50, 0x4b, 0x03, 0x04]),
  rar4: Buffer.from([0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x00]),
  rar5: Buffer.from([0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x01, 0x00]),
  sevenZ: Buffer.from([0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c])
});

/**
 * @param {Buffer} header
 * @returns {'zip' | 'rar' | '7z' | 'unknown'}
 */
export function detectMagic(header) {
  if (!header || header.length < 4) {
    return 'unknown';
  }
  if (header.subarray(0, 4).equals(MAGIC.zip)) {
    return 'zip';
  }
  if (header.length >= 8 && header.subarray(0, 8).equals(MAGIC.rar5)) {
    return 'rar';
  }
  if (header.length >= 7 && header.subarray(0, 7).equals(MAGIC.rar4)) {
    return 'rar';
  }
  if (header.length >= 6 && header.subarray(0, 6).equals(MAGIC.sevenZ)) {
    return '7z';
  }
  return 'unknown';
}

/**
 * @param {string} filePath
 */
export function isMultiVolumeName(filePath) {
  const name = basename(filePath).toLowerCase();
  return /\.part\d+\.rar$/.test(name) || /\.r\d{2}$/.test(name) || /\.\d{3}$/.test(name);
}

/**
 * @param {string | undefined} declared
 * @param {'zip' | 'rar' | '7z' | 'unknown'} actual
 */
export function reconcileArchiveType(declared, actual) {
  const normalized = declared === '7z' || declared === 'zip' || declared === 'rar' ? declared : null;
  if (actual === 'unknown') {
    return normalized || 'unknown';
  }
  return {
    type: actual,
    mismatch: Boolean(normalized && normalized !== actual)
  };
}
