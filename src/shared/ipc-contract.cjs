/**
 * Shared IPC contract. CommonJS so the sandboxed preload can require it
 * and the ESM main process can re-export it via createRequire.
 */

const INVOKE_CHANNELS = Object.freeze({
  APP_GET_INFO: 'app:getInfo',
  SETTINGS_GET: 'settings:get',
  SETTINGS_SET_LANGUAGE: 'settings:setLanguage',
  SETTINGS_SET_THEME: 'settings:setTheme',
  SETTINGS_SET_REDUCED_MOTION: 'settings:setReducedMotion'
});

const EVENT_CHANNELS = Object.freeze({
  SETTINGS_CHANGED: 'settings:changed'
});

const ALLOWED_INVOKE = Object.freeze(Object.values(INVOKE_CHANNELS));
const ALLOWED_EVENTS = Object.freeze(Object.values(EVENT_CHANNELS));

function isAllowedInvoke(channel) {
  return ALLOWED_INVOKE.includes(channel);
}

function isAllowedEvent(channel) {
  return ALLOWED_EVENTS.includes(channel);
}

module.exports = {
  INVOKE_CHANNELS,
  EVENT_CHANNELS,
  ALLOWED_INVOKE,
  ALLOWED_EVENTS,
  isAllowedInvoke,
  isAllowedEvent
};
