import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const contract = require('../../shared/ipc-contract.cjs');

export const INVOKE_CHANNELS = contract.INVOKE_CHANNELS;
export const EVENT_CHANNELS = contract.EVENT_CHANNELS;
export const ALLOWED_INVOKE = contract.ALLOWED_INVOKE;
export const ALLOWED_EVENTS = contract.ALLOWED_EVENTS;
export const isAllowedInvoke = contract.isAllowedInvoke;
export const isAllowedEvent = contract.isAllowedEvent;
