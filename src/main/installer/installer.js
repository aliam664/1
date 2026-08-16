import { existsSync, mkdirSync, renameSync, rmSync, statSync, cpSync } from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { AppError, ErrorCodes } from '../app/errors.js';
import { inspectGamePath } from '../assetto-corsa/validatePath.js';
import { analyzeExtractedTree, listRelativeFiles } from './structure.js';

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

  /**
   * Extract and analyse. Never writes into the game folder.
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
      if (!analysis.recognized) {
        return {
          sessionId: id,
          extractRoot,
          recognized: false,
          items: [],
          extras: analysis.extras,
          tree: summarizeTree(files)
        };
      }
      const items = analysis.items.map((item) => {
        const dest = path.join(game.path, ...item.suggestedDest.split('/'));
        return {
          ...item,
          destination: dest,
          exists: existsSync(dest),
          selected: true
        };
      });
      return {
        sessionId: id,
        extractRoot,
        recognized: true,
        items,
        extras: analysis.extras,
        tree: summarizeTree(files)
      };
    } catch (error) {
      rmSync(extractRoot, { recursive: true, force: true });
      throw error;
    }
  }

  /**
   * @param {{ sessionId: string, extractRoot: string, selections: Array<{ folderName: string, kind: string, sourceDir: string, suggestedDest: string, overwrite?: boolean, backup?: boolean }>, contentId?: string, name?: string, version?: string, sourceUrl?: string, archiveType?: string, checksum?: string }} plan
   */
  async function commit(plan) {
    const game = inspectGameOrThrow();
    const installed = [];
    const backups = [];
    try {
      for (const item of plan.selections) {
        const dest = path.join(game.path, ...item.suggestedDest.split('/'));
        if (existsSync(dest)) {
          if (item.backup) {
            const backup = `${dest}.bak-${Date.now()}`;
            renameSync(dest, backup);
            backups.push(backup);
          } else if (!item.overwrite) {
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
      cleanup(plan.extractRoot);
      logger.info('install committed', { count: installed.length });
      return { installed };
    } catch (error) {
      for (const record of installed) {
        try {
          rmSync(record.installPath, { recursive: true, force: true });
          repos.installed.remove(record.id);
        } catch {
          logger.warn('rollback cleanup failed', { id: record.id });
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
      if (!full.startsWith(root)) {
        throw new AppError(ErrorCodes.UNSAFE_ARCHIVE, 'Manifest path escaped the game folder');
      }
    }
    if (record.installPath && existsSync(record.installPath) && path.resolve(record.installPath).startsWith(root)) {
      rmSync(record.installPath, { recursive: true, force: true });
    }
    repos.installed.remove(id);
    return { id };
  }

  function inspectGameOrThrow() {
    const gamePath = getGamePath();
    if (!gamePath) {
      throw new AppError(ErrorCodes.GAME_PATH_INVALID, 'Assetto Corsa path is not set');
    }
    const { inspectGamePath } = waitInspect();
    const inspected = inspectGamePath(gamePath);
    if (!inspected.valid) {
      throw new AppError(ErrorCodes.GAME_PATH_INVALID, `Game path is incomplete: missing ${inspected.missing.join(', ')}`);
    }
    if (!inspected.writable) {
      throw new AppError(ErrorCodes.GAME_PATH_INVALID, 'Game folder is not writable. Run the launcher as administrator or move the library.');
    }
    return inspected;
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
    // Left in place for startup; individual session dirs are uuid folders.
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

function summarizeTree(files) {
  return files.slice(0, 80);
}
