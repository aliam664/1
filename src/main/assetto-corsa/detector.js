import { execFile } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { promisify } from 'node:util';
import { inspectGamePath } from './validatePath.js';
import { extractSteamLibraries, parseVdf } from './vdf.js';

const execFileAsync = promisify(execFile);
const STEAM_APP_ID = '244210';

const COMMON_PATHS = [
  'C:\\Program Files (x86)\\Steam\\steamapps\\common\\assettocorsa',
  'D:\\SteamLibrary\\steamapps\\common\\assettocorsa',
  'E:\\SteamLibrary\\steamapps\\common\\assettocorsa',
  'C:\\Program Files\\Steam\\steamapps\\common\\assettocorsa'
];

/**
 * @param {{ savedPath?: string, execFileImpl?: typeof execFileAsync, platform?: string }} [options]
 */
export async function detectAssettoCorsa(options = {}) {
  const attempts = [];
  const platform = options.platform || process.platform;

  if (options.savedPath) {
    const inspected = inspectGamePath(options.savedPath);
    attempts.push({ source: 'saved', ...inspected });
    if (inspected.valid) {
      return { found: true, path: inspected.path, source: 'saved', attempts };
    }
  }

  if (platform === 'win32') {
    const steamRoots = await readSteamRoots(options.execFileImpl || execFileAsync);
    for (const root of steamRoots) {
      const libraries = collectLibraries(root);
      for (const library of libraries) {
        const candidate = path.join(library, 'steamapps', 'common', 'assettocorsa');
        const inspected = inspectGamePath(candidate);
        attempts.push({ source: 'steam', ...inspected });
        if (inspected.valid && steamAppInstalled(library)) {
          return { found: true, path: inspected.path, source: 'steam', attempts };
        }
        if (inspected.valid) {
          return { found: true, path: inspected.path, source: 'steam-loose', attempts };
        }
      }
    }
  }

  for (const candidate of COMMON_PATHS) {
    const inspected = inspectGamePath(candidate);
    attempts.push({ source: 'common', ...inspected });
    if (inspected.valid) {
      return { found: true, path: inspected.path, source: 'common', attempts };
    }
  }

  return { found: false, path: null, source: null, attempts };
}

async function readSteamRoots(execImpl) {
  const queries = [
    ['query', 'HKCU\\Software\\Valve\\Steam', '/v', 'SteamPath'],
    ['query', 'HKLM\\SOFTWARE\\WOW6432Node\\Valve\\Steam', '/v', 'InstallPath']
  ];
  const roots = [];
  for (const args of queries) {
    try {
      const { stdout } = await execImpl('reg', args, { windowsHide: true, timeout: 4000 });
      const match = stdout.match(/REG_SZ\s+(.+)$/m);
      if (match) {
        roots.push(match[1].trim());
      }
    } catch {
      // Registry is optional; continue the fallback chain.
    }
  }
  return roots;
}

function collectLibraries(steamRoot) {
  const libraries = [steamRoot];
  const vdfPath = path.join(steamRoot, 'steamapps', 'libraryfolders.vdf');
  if (!existsSync(vdfPath)) {
    return libraries;
  }
  try {
    const parsed = parseVdf(readFileSync(vdfPath, 'utf8'));
    for (const folder of extractSteamLibraries(parsed)) {
      if (folder && !libraries.includes(folder)) {
        libraries.push(folder);
      }
    }
  } catch {
    // ignore malformed VDF
  }
  return libraries;
}

function steamAppInstalled(library) {
  return existsSync(path.join(library, 'steamapps', `appmanifest_${STEAM_APP_ID}.acf`));
}
