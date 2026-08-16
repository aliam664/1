import { cpSync, existsSync, mkdirSync, readdirSync, renameSync, rmSync, statSync } from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { AppError, ErrorCodes } from '../app/errors.js';
import { inspectGamePath } from '../assetto-corsa/validatePath.js';
import { analyzeExtractedTree, listRelativeFiles } from './structure.js';

function underRoot(root, candidate) {
  const resolvedRoot = path.resolve(root);
  const resolved = path.resolve(candidate);
  const prefix = resolvedRoot.endsWith(path.sep) ? resolvedRoot : `${resolvedRoot}${path.sep}`;
  return resolved === resolvedRoot || resolved.startsWith(prefix);
}

/**
 * @param {{
 *   repos: ReturnType<import('../database/repositories.js').createRepositories>,
 *   archive: { extract: Function },
 *   tempDir: string,
 *   logger: { info: Function, warn: Function, error: Function },
 *   getGamePath: () => string,
 *   onProgress?: (payload: object) => void
 * }} options
 */
export function createInstaller(options) {
  const { repos, archive, tempDir, logger, getGamePath, onProgress } = options;
  /** @type {Map<string, { extractRoot: string, items: object[] }>} */
  const sessions = new Map();

  function inspectGameOrThrow() {
    const gamePath = getGamePath();
    if (!gamePath) {
      throw new AppError(ErrorCodes.GAME_PATH_INVALID, 'Assetto Corsa path is not set');
    }
    const inspected = inspectGamePath(gamePath);
    if (!inspected.valid) {
      throw new AppError(
        ErrorCodes.GAME_PATH_INVALID,
        `Game path is incomplete: missing ${inspected.missing.join(', ')}`
      );
    }
    if (!inspected.writable) {
      throw new AppError(
        ErrorCodes.GAME_PATH_INVALID,
        'Game folder is not writable. Run the launcher as administrator or move the library.'
      );
    }
    return inspected;
  }

  /**
   * Extract and analyse. Never writes into the game folder.
   * The session stays in Main; the renderer only gets a sessionId.
   */
  async function analyze(archivePath, declaredType, password) {
    const game = inspectGameOrThrow();
    const id = randomUUID();
    const extractRoot = path.join(tempDir, id);
    mkdirSync(extractRoot, { recursive: true });
    onProgress?.({ phase: 'extract', sessionId: id });
    try {
      await archive.extract(archivePath, extractRoot, { declaredType, password });
      const files = listRelativeFiles(extractRoot);
      const analysis = analyzeExtractedTree(files, extractRoot);
      const items = analysis.items.map((item) => {
        const dest = path.join(game.path, ...item.suggestedDest.split('/'));
        return {
          ...item,
          destination: dest,
          exists: existsSync(dest),
          selected: true
        };
      });
      sessions.set(id, { extractRoot, items });
      return {
        sessionId: id,
        extractRoot: undefined,
        recognized: analysis.recognized,
        items: items.map((item) => ({
          folderName: item.folderName,
          kind: item.kind,
          suggestedDest: item.suggestedDest,
          exists: item.exists,
          selected: item.selected,
          relativeSource: item.relativeSource
        })),
        extras: analysis.extras,
        tree: files.slice(0, 80)
      };
    } catch (error) {
      rmSync(extractRoot, { recursive: true, force: true });
      throw error;
    }
  }

  /**
   * @param {{
   *   sessionId: string,
   *   selections: Array<{ folderName: string, overwrite?: boolean, backup?: boolean }>,
   *   contentId?: string,
   *   name?: string,
   *   version?: string,
   *   sourceUrl?: string,
   *   archiveType?: string,
   *   checksum?: string
   * }} plan
   */
  async function commit(plan) {
    if (!plan?.sessionId || !sessions.has(plan.sessionId)) {
      throw new AppError(ErrorCodes.NOT_FOUND, 'Install session expired. Analyse the archive again.');
    }
    const session = sessions.get(plan.sessionId);
    const game = inspectGameOrThrow();
    const installed = [];
    const backups = [];
    try {
      for (const choice of plan.selections || []) {
        const item = session.items.find((entry) => entry.folderName === choice.folderName);
        if (!item) {
          throw new AppError(ErrorCodes.NOT_FOUND, `Unknown install item ${choice.folderName}`);
        }
        if (!underRoot(session.extractRoot, item.sourceDir)) {
          throw new AppError(ErrorCodes.UNSAFE_ARCHIVE, 'Install source escaped the extract folder');
        }
        const dest = path.join(game.path, ...item.suggestedDest.split('/'));
        if (!underRoot(game.path, dest)) {
          throw new AppError(ErrorCodes.UNSAFE_ARCHIVE, 'Install destination escaped the game folder');
        }
        if (existsSync(dest)) {
          if (choice.backup) {
            const backup = `${dest}.bak-${Date.now()}`;
            renameSync(dest, backup);
            backups.push({ backup, dest });
          } else if (!choice.overwrite) {
            throw new AppError(ErrorCodes.CONFLICT, `${item.folderName} is already installed`);
          } else {
            rmSync(dest, { recursive: true, force: true });
          }
        }
        mkdirSync(path.dirname(dest), { recursive: true });
        onProgress?.({ phase: 'copy', item: item.folderName });
        cpSync(item.sourceDir, dest, { recursive: true });
        const filesManifest = listRelativeFiles(dest).map((rel) => `${item.suggestedDest}/${rel}`);
        const record = {
          id: plan.contentId ? `${plan.contentId}:${item.folderName}` : item.folderName,
          contentType: item.kind,
          name: plan.name || item.folderName,
          version: plan.version || '0.0.0',
          installPath: dest,
          installedAt: new Date().toISOString(),
          sourceUrl: plan.sourceUrl || '',
          archiveType: plan.archiveType || '',
          checksum: plan.checksum || '',
          sizeBytes: dirSize(dest),
          filesManifest
        };
        repos.installed.upsert(record);
        installed.push(record);
      }
      cleanup(session.extractRoot);
      sessions.delete(plan.sessionId);
      logger.info('install committed', { count: installed.length });
      return { installed };
    } catch (error) {
      for (const record of installed) {
        try {
          if (existsSync(record.installPath) && underRoot(game.path, record.installPath)) {
            rmSync(record.installPath, { recursive: true, force: true });
          }
          repos.installed.remove(record.id);
        } catch {
          logger.warn('rollback cleanup failed', { id: record.id });
        }
      }
      for (const pair of backups) {
        try {
          if (!existsSync(pair.dest) && existsSync(pair.backup)) {
            renameSync(pair.backup, pair.dest);
          }
        } catch {
          logger.warn('backup restore failed', { dest: pair.dest });
        }
      }
      throw error;
    }
  }

  function uninstall(id) {
    const record = repos.installed.get(id);
    if (!record) {
      throw new AppError(ErrorCodes.NOT_FOUND, 'Installed item not found');
    }
    const game = inspectGameOrThrow();
    const root = path.resolve(game.path);
    for (const rel of record.filesManifest) {
      const full = path.resolve(root, rel);
      if (!underRoot(root, full)) {
        throw new AppError(ErrorCodes.UNSAFE_ARCHIVE, 'Manifest path escaped the game folder');
      }
    }
    if (record.installPath && existsSync(record.installPath) && underRoot(root, record.installPath)) {
      rmSync(record.installPath, { recursive: true, force: true });
    }
    repos.installed.remove(id);
    return { id };
  }

  function cleanup(dir) {
    if (dir && dir.startsWith(tempDir) && existsSync(dir)) {
      rmSync(dir, { recursive: true, force: true });
    }
  }

  function purgeOrphans() {
    if (!existsSync(tempDir)) {
      return;
    }
    for (const name of readdirSync(tempDir)) {
      const full = path.join(tempDir, name);
      try {
        rmSync(full, { recursive: true, force: true });
      } catch {
        logger.warn('orphan temp cleanup failed', { name });
      }
    }
    sessions.clear();
  }

  return { analyze, commit, uninstall, cleanup, purgeOrphans, listInstalled: () => repos.installed.list() };
}

function dirSize(dir) {
  try {
    return statSync(dir).size;
  } catch {
    return 0;
  }
}

export { underRoot };
