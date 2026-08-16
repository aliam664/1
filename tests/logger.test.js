import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { describe, it } from 'node:test';
import { createLogger } from '../src/main/logger/logger.js';

describe('logger', () => {
  it('writes JSON lines and redacts secret-looking fields', async () => {
    const dir = mkdtempSync(path.join(tmpdir(), 'sc-logs-'));
    const logger = createLogger(dir, { level: 'info' });
    logger.info('hello', { user: 'alex', password: 'hunter2', token: 'abc' });
    await logger.close();
    const raw = readFileSync(path.join(dir, 'stockcorsa.log'), 'utf8');
    const line = JSON.parse(raw.trim());
    assert.equal(line.message, 'hello');
    assert.equal(line.fields.user, 'alex');
    assert.equal(line.fields.password, '[redacted]');
    assert.equal(line.fields.token, '[redacted]');
    assert.equal(raw.includes('hunter2'), false);
  });
});
