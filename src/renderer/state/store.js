const state = {
  route: 'home',
  language: 'fa',
  theme: 'dark',
  reducedMotion: false,
  online: typeof navigator === 'undefined' ? true : navigator.onLine,
  isElectron: false,
  appInfo: null,
  settings: null,
  catalog: { items: [], featured: [], itemCount: 0 },
  downloads: [],
  gameStatus: null,
  updateInfo: null,
  search: '',
  error: null
};

const listeners = new Set();

export function getState() {
  return state;
}

export function setState(patch) {
  Object.assign(state, patch);
  listeners.forEach((listener) => listener(state));
}

export function subscribe(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
