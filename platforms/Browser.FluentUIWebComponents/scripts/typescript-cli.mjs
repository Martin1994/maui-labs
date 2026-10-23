import { spawnSync } from 'node:child_process';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('..', import.meta.url));

/** Invoke npm's installed compiler executable, whether its implementation is JS or native. */
export function runTypeScript(args, { allowFailure = false, cwd = root, packageRoot = root } = {}) {
    const executable = resolve(packageRoot, 'node_modules/.bin', process.platform === 'win32' ? 'tsc.cmd' : 'tsc');
    let command = executable;
    let commandArgs = [...args, '--pretty', 'false'];
    if (process.platform === 'win32') {
        // Windows npm executables are CMD shims; do not assume bin/tsc is a JS file.
        const quoted = [executable, ...commandArgs].map(argument => {
            if (/["\r\n%]/u.test(argument)) {
                throw new Error('Unsupported character in a Windows compiler argument');
            }
            return `"${argument}"`;
        });
        command = process.env.ComSpec || 'cmd.exe';
        commandArgs = ['/d', '/s', '/c', `"${quoted.join(' ')}"`];
    }
    const result = spawnSync(command, commandArgs, { cwd, encoding: 'utf8' });
    if (result.error) {
        throw result.error;
    }
    if (result.signal || (!allowFailure && result.status !== 0)) {
        throw new Error(`TypeScript compiler failed (${result.signal ?? result.status})\n${result.stdout}${result.stderr}`);
    }
    return result;
}