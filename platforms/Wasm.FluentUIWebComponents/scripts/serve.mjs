import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { resolve, extname, sep } from 'node:path';

const root = fileURLToPath(new URL('..', import.meta.url));
let directory = resolve(root, 'artifacts/wwwroot');
try {
    directory = (await readFile(resolve(root, 'artifacts/latest-webroot'), 'utf8')).trim();
} catch (error) {
    if (error.code !== 'ENOENT') {
        throw error;
    }
}
directory = resolve(process.argv[2] ?? directory);
await readFile(resolve(directory, 'index.html'));
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.wasm': 'application/wasm', '.css': 'text/css' };
const server = createServer(async (request, response) => {
    try {
        if (!['GET', 'HEAD'].includes(request.method)) {
            response.writeHead(405).end();
            return;
        }
        const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
        const file = resolve(directory, `.${pathname === '/' ? '/index.html' : pathname}`);
        if (!file.startsWith(directory + sep)) {
            response.writeHead(403).end();
            return;
        }
        const bytes = await readFile(file);
        response.writeHead(200, { 'Content-Type': mime[extname(file)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
        response.end(request.method === 'HEAD' ? undefined : bytes);
    } catch {
        response.writeHead(404).end();
    }
});
server.listen(Number(process.argv[3] ?? 8768), '127.0.0.1', () => {
    return console.log(`MAUI sample: http://127.0.0.1:${server.address().port}`);
});