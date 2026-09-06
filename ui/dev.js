const { spawn } = require('child_process');
const http = require('http');
const path = require('path');

const ANGULAR_DIR = path.join(__dirname, 'angular');
const RUNTIME_DIR = path.join(__dirname, 'runtime');
const BOOTSTRAPPER_DIR = path.join(__dirname, 'bootstrapper');
const ANGULAR_URL = 'http://localhost:4200';
const POLL_INTERVAL = 1000;
const MAX_WAIT = 60000;

const children = [];

function log(tag, message) {
  const timestamp = new Date().toLocaleTimeString();
  console.log(`[${timestamp}] [${tag}] ${message}`);
}

// Never forward DYLD_* to child processes: a DYLD_LIBRARY_PATH pointing at
// Homebrew (a common ~/.zprofile export) makes dyld resolve ImageIO's libpng
// against Homebrew's incompatible copy inside the bootstrapper, which crashes
// tray-icon creation with SIGBUS on macOS.
function childEnv() {
  const env = { ...process.env, FORCE_COLOR: '1' };
  for (const key of Object.keys(env)) {
    if (key.startsWith('DYLD_')) {
      delete env[key];
    }
  }
  return env;
}

function run(command, args, cwd, tag) {
  const child = spawn(command, args, {
    cwd,
    stdio: ['ignore', 'pipe', 'pipe'],
    shell: true,
    env: childEnv(),
  });

  children.push(child);

  child.stdout.on('data', (data) => {
    data.toString().trim().split('\n').forEach((line) => {
      if (line.trim()) log(tag, line);
    });
  });

  child.stderr.on('data', (data) => {
    data.toString().trim().split('\n').forEach((line) => {
      if (line.trim()) log(tag, line);
    });
  });

  child.on('close', (code) => {
    log(tag, `exited with code ${code}`);
  });

  return child;
}

function waitForServer(url, timeout) {
  return new Promise((resolve, reject) => {
    const start = Date.now();

    function check() {
      http.get(url, (res) => {
        resolve();
      }).on('error', () => {
        if (Date.now() - start > timeout) {
          reject(new Error(`Timeout waiting for ${url}`));
        } else {
          setTimeout(check, POLL_INTERVAL);
        }
      });
    }

    check();
  });
}

function cleanup() {
  children.forEach((child) => {
    if (!child.killed) {
      child.kill();
    }
  });
}

process.on('SIGINT', () => {
  log('dev', 'Shutting down...');
  cleanup();
  process.exit(0);
});

process.on('SIGTERM', () => {
  cleanup();
  process.exit(0);
});

async function main() {
  log('dev', 'Building the framework-free runtime...');
  const buildRuntime = spawn('npm', ['run', 'build'], {
    cwd: RUNTIME_DIR,
    stdio: 'inherit',
    shell: true,
  });

  await new Promise((resolve, reject) => {
    buildRuntime.on('close', (code) => {
      if (code === 0) resolve();
      else reject(new Error(`Runtime build failed with code ${code}`));
    });
  });

  log('dev', 'Starting Angular dev server...');
  run('npx', ['ng', 'serve', 'desktop-ui'], ANGULAR_DIR, 'angular');

  log('dev', `Waiting for Angular at ${ANGULAR_URL}...`);
  await waitForServer(ANGULAR_URL, MAX_WAIT);
  log('dev', 'Angular is ready');

  log('dev', 'Starting bootstrapper (tauri dev)...');
  const bootstrapper = run('npx', ['tauri', 'dev'], BOOTSTRAPPER_DIR, 'bootstrapper');

  bootstrapper.on('close', () => {
    log('dev', 'Bootstrapper closed, shutting down...');
    cleanup();
    process.exit(0);
  });
}

main().catch((err) => {
  console.error(err.message);
  cleanup();
  process.exit(1);
});
