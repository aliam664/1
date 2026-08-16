/**
 * Security policy constants. This file must stay free of the `electron`
 * import so unit tests can load it under plain Node.
 */

export const WEB_PREFERENCES = Object.freeze({
  contextIsolation: true,
  nodeIntegration: false,
  nodeIntegrationInWorker: false,
  nodeIntegrationInSubFrames: false,
  sandbox: true,
  webSecurity: true,
  allowRunningInsecureContent: false,
  experimentalFeatures: false,
  webviewTag: false,
  spellcheck: false,
  // Navigation / popups are also locked in window.js.
  safeDialogs: true
});

/**
 * Strict CSP. No unsafe-inline, no unsafe-eval, no remote origins.
 * Renderer network is forbidden; Main performs every HTTP request.
 */
export const CONTENT_SECURITY_POLICY = [
  "default-src 'self'",
  "script-src 'self'",
  "style-src 'self'",
  "img-src 'self' data:",
  "font-src 'self'",
  "connect-src 'none'",
  "object-src 'none'",
  "base-uri 'self'",
  "form-action 'none'",
  "frame-ancestors 'none'",
  "worker-src 'none'",
  "media-src 'none'",
  "upgrade-insecure-requests"
].join('; ');

export const PERMISSIONS_DENIED = Object.freeze([
  'clipboard-read',
  'clipboard-sanitized-write',
  'display-capture',
  'geolocation',
  'idle-detection',
  'media',
  'mediaKeySystem',
  'midi',
  'midiSysex',
  'notifications',
  'pointerLock',
  'openExternal',
  'serial',
  'usb',
  'hid',
  'fullscreen'
]);

/**
 * @param {string} policy
 * @returns {{ ok: true } | { ok: false, reason: string }}
 */
export function assertStrictCsp(policy) {
  const forbidden = ['unsafe-inline', 'unsafe-eval', 'unsafe-hashes', 'wasm-unsafe-eval'];
  const lower = policy.toLowerCase();
  for (const token of forbidden) {
    if (lower.includes(token)) {
      return { ok: false, reason: `CSP contains forbidden token: ${token}` };
    }
  }
  if (!/script-src[^;]*'self'/.test(lower)) {
    return { ok: false, reason: "CSP script-src must include 'self'" };
  }
  if (!/connect-src[^;]*'none'/.test(lower)) {
    return { ok: false, reason: "CSP connect-src must be 'none' so the renderer has no network" };
  }
  return { ok: true };
}
