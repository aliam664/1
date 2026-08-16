/**
 * Rebuild native modules against Electron when both are present.
 * better-sqlite3 is optional: sql.js is the portable fallback and writes
 * the same SQLite file format.
 */
import { spawnSync } from 'node:child_process';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);

try {
  require.resolve('better-sqlite3');
  require.resolve('electron');
} catch {
  process.stdout.write('postinstall: no native Electron rebuild needed (sql.js adapter in use)\n');
  process.exit(0);
}

const result = spawnSync('npx', ['electron-builder', 'install-app-deps'], {
  stdio: 'inherit',
  shell: process.platform === 'win32'
});
process.exit(result.status ?? 0);
