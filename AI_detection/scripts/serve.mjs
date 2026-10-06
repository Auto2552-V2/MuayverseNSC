/**
 * Static server for the test page.
 *
 *   npm run serve          # http://localhost:8000/web/
 *   npm run serve -- 8080
 *
 * Serves the REPO ROOT, not web/, because the page loads the model from
 * ../tfjs_build/ -- serving web/ alone makes those 404 with no obvious cause.
 *
 * A server is needed at all because getUserMedia requires a secure context:
 * localhost counts, file:// does not, so opening web/index.html directly gives
 * a camera-permission failure that looks like a browser problem.
 *
 * Binds 127.0.0.1 only. This serves the whole repo including training data, so
 * it has no business being reachable from the network.
 */
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const port = Number(process.argv[2] || 8000);

const TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.bin': 'application/octet-stream',
  '.task': 'application/octet-stream',
  '.wasm': 'application/wasm',
  '.css': 'text/css; charset=utf-8',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.svg': 'image/svg+xml',
};

http.createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  let target = path.join(root, url);

  // Reject anything that escapes the root before touching the filesystem.
  // Trivial here (localhost, debug tool) but a traversal bug in a server that
  // serves a whole repo is not worth leaving in on purpose.
  if (!path.resolve(target).startsWith(root)) {
    res.writeHead(403).end('forbidden');
    return;
  }

  if (fs.existsSync(target) && fs.statSync(target).isDirectory()) {
    target = path.join(target, 'index.html');
  }
  if (!fs.existsSync(target)) {
    res.writeHead(404, { 'content-type': 'text/plain; charset=utf-8' })
       .end(`404 ${url}`);
    console.log(`404  ${url}`);
    return;
  }

  const type = TYPES[path.extname(target).toLowerCase()] || 'application/octet-stream';
  const body = fs.readFileSync(target);
  // no-store: the whole point of this server is reloading after an edit, and a
  // cached pose-detector.js makes it look as though the edit did nothing.
  res.writeHead(200, { 'content-type': type, 'cache-control': 'no-store' }).end(body);
  console.log(`200  ${url}  ${type}  ${(body.length / 1024).toFixed(0)} KB`);
}).listen(port, '127.0.0.1', () => {
  console.log(`serving ${root}`);
  console.log(`open    http://localhost:${port}/web/`);
});
