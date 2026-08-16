/**
 * Typed application errors. IPC maps these to a public envelope
 * without leaking stack traces.
 */
export class AppError extends Error {
  /**
   * @param {string} code
   * @param {string} message
   * @param {Record<string, unknown>} [extras]
   */
  constructor(code, message, extras = {}) {
    super(message);
    this.name = 'AppError';
    this.code = code;
    this.extras = extras;
  }

  toPublic() {
    return { code: this.code, message: this.message, ...this.extras };
  }
}

export const ErrorCodes = Object.freeze({
  INTERNAL: 'INTERNAL',
  IO: 'IO',
  NETWORK: 'NETWORK',
  TIMEOUT: 'TIMEOUT',
  RATE_LIMIT: 'RATE_LIMIT',
  OFFLINE: 'OFFLINE',
  NOT_FOUND: 'NOT_FOUND',
  CHECKSUM: 'CHECKSUM',
  UNSAFE_ARCHIVE: 'UNSAFE_ARCHIVE',
  MULTI_VOLUME: 'MULTI_VOLUME',
  ENCRYPTED_ARCHIVE: 'ENCRYPTED_ARCHIVE',
  UNRECOGNIZED_STRUCTURE: 'UNRECOGNIZED_STRUCTURE',
  GAME_PATH_INVALID: 'GAME_PATH_INVALID',
  DISK_SPACE: 'DISK_SPACE',
  DOWNLOAD_HTML: 'DOWNLOAD_HTML',
  RESUME_UNSUPPORTED: 'RESUME_UNSUPPORTED',
  CONFLICT: 'CONFLICT',
  UPDATE_NONE: 'UPDATE_NONE',
  HOST_UNTRUSTED: 'HOST_UNTRUSTED'
});
