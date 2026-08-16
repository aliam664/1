/**
 * Static preview of the renderer. Used when Electron cannot open a window
 * (CI, web review). The page talks to the browser fallback bridge and
 * never receives Node privileges.
 */
import http from 'node:http';
import { createReadStream, existsSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '../src/renderer');
const HOST = '0.0.0.0';
const PORT = Number(process.env.PORT || 4173);

const TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.woff2': 'font/woff2',
  '.png': 'image/png',
  '.ico': 'image/x-icon'
};

function safeJoin(root, requestPath) {
  const decoded = decodeURIComponent((requestPath || '/').split('?')[0]);
  const relative = decoded === '/' ? 'index.html' : decoded.replace(/^\/+/, '');
  const resolved = path.resolve(root, relative);
  if (!resolved.startsWith(root + path.sep) && resolved !== root) {
    return null;
  }
  return resolved;
}

const server = http.createServer((req, res) => {
  const filePath = safeJoin(ROOT, req.url || '/');
  if (!filePath || !existsSync(filePath) || !statSync(filePath).isFile()) {
    res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end('Not found');
    return;
  }
  const type = TYPES[path.extname(filePath)] || 'application/octet-stream';
  res.writeHead(200, {
    'Content-Type': type,
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff'
  });
  // Electron keeps frame-ancestors 'none'. The preview must be embeddable.
  if (path.extname(filePath) === '.html') {
    const html = readFileSync(filePath, 'utf8').replace(/frame-ancestors\s+'none';\s*/g, '');
    res.end(html);
    return;
  }
  createReadStream(filePath).pipe(res);
});

server.listen(PORT, HOST, () => {
  process.stdout.write(`StockCorsa preview http://${HOST}:${PORT}\n`);
});
