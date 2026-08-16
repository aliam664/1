import { existsSync } from 'node:fs';
import path from 'node:path';
import { AppError, ErrorCodes } from '../app/errors.js';
import { inspectGamePath } from './validatePath.js';

/**
 * @param {string} gamePath
 * @returns {string}
 */
export function resolveGameExecutable(gamePath) {
  const inspected = inspectGamePath(gamePath);
  if (!inspected.valid) {
    throw new AppError(
      ErrorCodes.GAME_PATH_INVALID,
      `Cannot launch: missing ${inspected.missing.join(', ')}`
    );
  }
  const exe = ['AssettoCorsa.exe', 'acs.exe']
    .map((name) => path.join(inspected.path, name))
    .find((candidate) => existsSync(candidate));
  if (!exe) {
    throw new AppError(ErrorCodes.GAME_PATH_INVALID, 'Game executable was not found');
  }
  return exe;
}
