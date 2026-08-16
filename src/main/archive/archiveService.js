import { spawn } from 'node:child_process';
import { closeSync, createReadStream, existsSync, mkdirSync, openSync, readFileSync, readSync, rmSync, statSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { unzipSync } from 'fflate';
import { APP_CONFIG } from '../config/appConfig.js';
import { AppError, ErrorCodes } from '../app/errors.js';
import { resolveSafeDestination } from '../filesystem/safePath.js';
import { detectMagic, isMultiVolumeName } from './magic.js';

/**
 * @param {string} filePath
 */
export function readMagic(filePath) {
  const fd = openSync(filePath, 'r');
  const header = Buffer.alloc(16);
  try {
    readSync(fd, header, 0, 16, 0);
  } finally {
    closeSync(fd);
  }
  return detectMagic(header);
}

/**
 * @param {{
 *   logger: { info: Function, warn: Function, error: Function },
 *   externalToolPath?: string,
 *   onProgress?: (info: { fileName?: string, phase: string }) => void
 * }} options
 */
export function createArchiveService(options) {
  const { logger, onProgress } = options;

  /**
   * @param {string} archivePath
   * @param {string} targetDir
   * @param {{ password?: string, declaredType?: string }} [extractOptions]
   */
  async function extract(archivePath, targetDir, extractOptions = {}) {
    if (!existsSync(archivePath)) {
      throw new AppError(ErrorCodes.NOT_FOUND, 'Archive file is missing');
    }
    if (isMultiVolumeName(archivePath)) {
      throw new AppError(
        ErrorCodes.MULTI_VOLUME,
        'This mod is multi-volume and needs the optional WinRAR / 7-Zip helper'
      );
    }
    const actual = readMagic(archivePath);
    if (extractOptions.declaredType && extractOptions.declaredType !== actual && actual !== 'unknown') {
      logger.warn('archive type mismatch, using magic bytes', {
        declared: extractOptions.declaredType,
        actual
      });
    }
    const type = actual === 'unknown' ? extractOptions.declaredType : actual;
    mkdirSync(targetDir, { recursive: true });
    onProgress?.({ phase: 'extract', fileName: path.basename(archivePath) });

    if (type === 'zip') {
      return extractZip(archivePath, targetDir);
    }
    if (type === 'rar') {
      return extractRar(archivePath, targetDir, extractOptions.password);
    }
    if (type === '7z') {
      return extract7z(archivePath, targetDir, options.externalToolPath);
    }
    throw new AppError(ErrorCodes.UNSAFE_ARCHIVE, 'Unknown archive type');
  }

  function extractZip(archivePath, targetDir) {
    const stat = statSync(archivePath);
    if (stat.size > 512 * 1024 * 1024) {
      const tool = options.externalToolPath;
      if (tool) {
        return runExternal(tool, ['x', `-o${targetDir}`, '-y', archivePath], targetDir);
      }
      throw new AppError(ErrorCodes.IO, 'ZIP is too large for the built-in extractor; set a 7-Zip path in Settings');
    }
    const unzipped = unzipSync(readFileSync(archivePath));
    const names = Object.keys(unzipped);
    if (names.length > APP_CONFIG.archive.maxEntries) {
      throw new AppError(ErrorCodes.UNSAFE_ARCHIVE, 'Archive has too many entries');
    }
    let total = 0;
    for (const name of names) {
      if (name.endsWith('/')) {
        continue;
      }
      const dest = resolveSafeDestination(name, targetDir);
      const data = unzipped[name];
      total += data.byteLength;
      if (total > APP_CONFIG.archive.maxExtractedBytes) {
        rmSync(targetDir, { recursive: true, force: true });
        throw new AppError(ErrorCodes.UNSAFE_ARCHIVE, 'Archive exceeds extract size limit');
      }
      if (stat.size > 0 && total / stat.size > APP_CONFIG.archive.maxCompressionRatio) {
        rmSync(targetDir, { recursive: true, force: true });
        throw new AppError(ErrorCodes.UNSAFE_ARCHIVE, 'Suspicious compression ratio');
      }
      mkdirSync(path.dirname(dest), { recursive: true });
      writeFileSync(dest, data);
      onProgress?.({ phase: 'extract', fileName: name });
    }
    return { type: 'zip', files: names.filter((name) => !name.endsWith('/')).length };
  }

  async function extractRar(archivePath, targetDir, password) {
    const { createExtractorFromFile } = await import('node-unrar-js');
    let extractor;
    try {
      extractor = await createExtractorFromFile({
        filepath: archivePath,
        targetPath: targetDir,
        password: password || undefined
      });
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      if (/password|crypt/i.test(message)) {
        throw new AppError(ErrorCodes.ENCRYPTED_ARCHIVE, 'Archive is encrypted');
      }
      if (options.externalToolPath) {
        logger.warn('wasm rar failed, trying external tool', { message });
        return runExternal(options.externalToolPath, buildExternalArgs(archivePath, targetDir, password), targetDir);
      }
      throw new AppError(ErrorCodes.IO, message);
    }
    const list = extractor.getFileList();
    const headers = list.fileHeaders || list.arcHeader?.fileHeaders || [];
    if (headers.some((header) => header.flags?.encrypted || header.encrypted)) {
      if (!password) {
        throw new AppError(ErrorCodes.ENCRYPTED_ARCHIVE, 'Archive is encrypted');
      }
    }
    if ((headers.length || 0) > APP_CONFIG.archive.maxEntries) {
      throw new AppError(ErrorCodes.UNSAFE_ARCHIVE, 'Archive has too many entries');
    }
    for (const header of headers) {
      if (header.flags?.directory || header.name?.endsWith('/')) {
        continue;
      }
      resolveSafeDestination(header.name, targetDir);
      onProgress?.({ phase: 'extract', fileName: header.name });
    }
    const extracted = extractor.extract();
    if (extracted.state !== 'SUCCESS' && extracted.state != null && extracted.state !== 0) {
      throw new AppError(ErrorCodes.IO, 'RAR extraction failed');
    }
    return { type: 'rar', files: headers.length };
  }

  function extract7z(archivePath, targetDir, toolPath) {
    if (!toolPath) {
      throw new AppError(
        ErrorCodes.IO,
        '7z archives need 7-Zip. Set the helper path in Settings.'
      );
    }
    return runExternal(toolPath, ['x', `-o${targetDir}`, '-y', archivePath], targetDir);
  }

  function buildExternalArgs(archivePath, targetDir, password) {
    const args = ['x', `-o${targetDir}`, '-y'];
    if (password) {
      args.push(`-p${password}`);
    }
    args.push(archivePath);
    return args;
  }

  function runExternal(toolPath, args, targetDir) {
    if (!existsSync(toolPath)) {
      throw new AppError(ErrorCodes.NOT_FOUND, 'External archive tool was not found');
    }
    return new Promise((resolve, reject) => {
      const child = spawn(toolPath, args, { windowsHide: true });
      child.on('error', (error) => reject(new AppError(ErrorCodes.IO, error.message)));
      child.on('close', (code) => {
        if (code === 0) {
          resolve({ type: 'external', files: 0, targetDir });
        } else {
          reject(new AppError(ErrorCodes.IO, `External extractor exited with ${code}`));
        }
      });
    });
  }

  return { extract, readMagic };
}

/**
 * Consume the first 16 bytes without holding the file.
 * @param {string} filePath
 */
export function peekHeader(filePath) {
  return new Promise((resolve, reject) => {
    const stream = createReadStream(filePath, { start: 0, end: 15 });
    const chunks = [];
    stream.on('data', (chunk) => chunks.push(chunk));
    stream.on('error', reject);
    stream.on('end', () => resolve(Buffer.concat(chunks)));
  });
}
