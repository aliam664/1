import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';

/**
 * Stream a file through SHA-256. Never loads the whole file into RAM.
 * @param {string} filePath
 * @returns {Promise<string>}
 */
export function sha256File(filePath) {
  return new Promise((resolve, reject) => {
    const hash = createHash('sha256');
    const stream = createReadStream(filePath);
    stream.on('data', (chunk) => hash.update(chunk));
    stream.on('error', reject);
    stream.on('end', () => resolve(hash.digest('hex')));
  });
}

export function sha256Buffer(buffer) {
  return createHash('sha256').update(buffer).digest('hex');
}
