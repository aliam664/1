import { accessSync, constants, existsSync } from 'node:fs';
import path from 'node:path';

const REQUIRED = [
  { id: 'content', rel: ['content'] },
  { id: 'cars', rel: ['content', 'cars'] },
  { id: 'tracks', rel: ['content', 'tracks'] }
];

/**
 * @param {string} gamePath
 */
export function inspectGamePath(gamePath) {
  const resolved = path.resolve(gamePath || '');
  /** @type {{ id: string, path: string, ok: boolean }[]} */
  const checks = [];
  const exeCandidates = ['AssettoCorsa.exe', 'acs.exe'];
  const exe = exeCandidates
    .map((name) => path.join(resolved, name))
    .find((candidate) => existsSync(candidate));
  checks.push({ id: 'executable', path: exe || path.join(resolved, 'AssettoCorsa.exe'), ok: Boolean(exe) });
  for (const item of REQUIRED) {
    const full = path.join(resolved, ...item.rel);
    checks.push({ id: item.id, path: full, ok: existsSync(full) });
  }
  let writable = false;
  try {
    accessSync(path.join(resolved, 'content'), constants.W_OK);
    writable = true;
  } catch {
    writable = false;
  }
  checks.push({ id: 'writable', path: path.join(resolved, 'content'), ok: writable });
  const missing = checks.filter((item) => !item.ok).map((item) => item.id);
  return {
    path: resolved,
    valid: missing.filter((id) => id !== 'writable').length === 0,
    writable,
    missing,
    checks
  };
}

export function isValidGamePath(gamePath) {
  return inspectGamePath(gamePath).valid;
}
