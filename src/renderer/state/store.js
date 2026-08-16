/**
 * Tiny observable store. No framework.
 */

const state = {
  route: 'home',
  language: 'fa',
  theme: 'dark',
  reducedMotion: false,
  online: typeof navigator === 'undefined' ? true : navigator.onLine,
  isElectron: false,
  appInfo: null,
  error: null
};

const listeners = new Set();

export function getState() {
  return state;
}

/**
 * @param {Partial<typeof state>} patch
 */
export function setState(patch) {
  Object.assign(state, patch);
  listeners.forEach((listener) => listener(state));
}

/**
 * @param {(snapshot: typeof state) => void} listener
 * @returns {() => void}
 */
export function subscribe(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
