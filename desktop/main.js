const { app, BrowserWindow, Menu, dialog, ipcMain, safeStorage } = require('electron');
const { spawn } = require('node:child_process');
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');

// The host listens on a free port and only answers requests that carry this token, so neither another
// program on this machine nor a web page in a browser can use it to read files or run tests.
const HOST_TOKEN = crypto.randomBytes(32).toString('hex');
let hostProcess = null;
let activeRun = null;
let hostReadyPromise = null;
let hostBaseUrl = null;

function createWindow() {
  const win = new BrowserWindow({
    width: 1200,
    height: 840,
    minWidth: 980,
    minHeight: 700,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
    },
    backgroundColor: '#f4efe7',
  });

  win.loadFile(path.join(__dirname, 'dist', 'index.html'));
}

app.whenReady().then(() => {
  createWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createWindow();
    }
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit();
  }
});

ipcMain.handle('choose-folder', async () => {
  const result = await dialog.showOpenDialog({
    properties: ['openDirectory', 'createDirectory'],
  });

  if (result.canceled || result.filePaths.length === 0) {
    return null;
  }

  const folderPath = result.filePaths[0];
  const collectionPath = path.join(folderPath, 'collection.yaml');
  const hasCollection = fs.existsSync(collectionPath);

  return {
    folderPath,
    hasCollection,
  };
});

ipcMain.handle('init-collection', async (_, payload) => {
  return hostRequest(
    'POST',
    '/api/collection/init',
    { folderPath: payload.folderPath },
    { collectionName: payload.collectionName },
  );
});

ipcMain.handle('import-openapi', async (_, payload) => {
  return hostRequest(
    'POST',
    '/api/collection/import-openapi',
    { folderPath: payload.folderPath },
    {
      collectionName: payload.collectionName,
      specificationUrl: payload.specificationUrl,
    },
  );
});

// Streams the run: every event but the last is forwarded to the renderer as 'run-progress'.
ipcMain.handle('run-tests', async (event, payload) => {
  activeRun?.abort();
  const controller = new AbortController();
  activeRun = controller;
  let completed = null;
  try {
    const response = await hostFetch(
      'POST',
      '/api/run',
      { folderPath: payload.folderPath },
      {
        localSecrets: readLocalSecrets(payload.folderPath),
        environment: payload.environment || null,
        tests: payload.tests?.length ? payload.tests : null,
      },
      controller.signal,
    );
    for await (const item of readJsonLines(response.body)) {
      if (item.type === 'failed') throw new Error(item.message);
      if (item.type === 'completed') completed = item;
      else if (!event.sender.isDestroyed()) event.sender.send('run-progress', item);
    }
  } catch (error) {
    if (controller.signal.aborted) return { cancelled: true };
    throw error;
  } finally {
    if (activeRun === controller) activeRun = null;
  }

  if (!completed) throw new Error('The run ended without a result.');
  return {
    exitCode: completed.exitCode,
    stdout: completed.report || '',
    stderr: '',
    result: completed.result || null,
  };
});

// Closing the request cancels the run on the host.
ipcMain.handle('cancel-run', async () => {
  activeRun?.abort();
});

ipcMain.handle('preview-step', async (_, payload) =>
  hostRequest(
    'POST',
    '/api/tests/preview',
    { folderPath: payload.folderPath },
    {
      test: payload.test,
      stepIndex: payload.stepIndex,
      localSecrets: readLocalSecrets(payload.folderPath),
      environment: payload.environment || null,
    },
  ),
);

ipcMain.handle('list-tests', async (_, payload) => {
  return hostRequest('GET', '/api/tests', {
    folderPath: payload.folderPath,
  });
});

ipcMain.handle('get-collection', async (_, payload) => {
  return hostRequest('GET', '/api/collection', {
    folderPath: payload.folderPath,
  });
});

ipcMain.handle('save-collection-variables', async (_, payload) => {
  return hostRequest(
    'POST',
    '/api/collection/variables',
    {
      folderPath: payload.folderPath,
    },
    payload.variables || {},
  );
});

ipcMain.handle('save-collection-settings', async (_, payload) => {
  return hostRequest(
    'POST',
    '/api/collection/settings',
    {
      folderPath: payload.folderPath,
    },
    {
      variables: payload.variables || {},
      connections: payload.connections || {},
      secrets: payload.secrets || {},
    },
  );
});

ipcMain.handle('check-folder', async (_, payload) => ({
  exists: fs.existsSync(payload.folderPath),
  hasCollection: fs.existsSync(path.join(payload.folderPath, 'collection.yaml')),
}));

ipcMain.handle('list-shared', async (_, payload) => {
  const data = await hostRequest('GET', '/api/shared', { folderPath: payload.folderPath });
  return data?.shared || [];
});

ipcMain.handle('get-shared', async (_, payload) =>
  hostRequest('GET', `/api/shared/${encodeURIComponent(payload.fileName)}`, {
    folderPath: payload.folderPath,
  }),
);

ipcMain.handle('save-shared', async (_, payload) =>
  hostRequest(
    'POST',
    '/api/shared',
    { folderPath: payload.folderPath },
    {
      fileName: payload.fileName,
      name: payload.name,
      description: payload.description,
      run: payload.run,
      steps: payload.steps || [],
    },
  ),
);

ipcMain.handle('delete-shared', async (_, payload) =>
  hostRequest('DELETE', `/api/shared/${encodeURIComponent(payload.fileName)}`, {
    folderPath: payload.folderPath,
  }),
);

ipcMain.handle('get-aggregations', async () => {
  const data = await hostRequest('GET', '/api/assertions/aggregations');
  return data?.aggregations || [];
});

ipcMain.handle('get-secret-providers', async () => {
  const data = await hostRequest('GET', '/api/secrets/providers');
  return data?.providers || [];
});

// Values of secrets whose provider is "local" are kept encrypted with the operating system's
// secure storage (Keychain / DPAPI / libsecret). The renderer can write or delete them but never read them back.
ipcMain.handle('list-local-secrets', async (_, payload) =>
  Object.keys(readSecretStore()[payload.folderPath] || {}),
);

ipcMain.handle('set-local-secret', async (_, payload) => {
  assertSecureStorage();
  const store = readSecretStore();
  store[payload.folderPath] = {
    ...store[payload.folderPath],
    [payload.name]: safeStorage.encryptString(payload.value).toString('base64'),
  };
  writeSecretStore(store);
});

ipcMain.handle('delete-local-secret', async (_, payload) => {
  const store = readSecretStore();
  if (store[payload.folderPath]) {
    delete store[payload.folderPath][payload.name];
    writeSecretStore(store);
  }
});

ipcMain.handle('get-test-case', async (_, payload) => {
  return hostRequest('GET', `/api/tests/${encodeTestPath(payload.fileName)}`, {
    folderPath: payload.folderPath,
  });
});

ipcMain.handle('save-test-case', async (_, payload) => {
  return hostRequest(
    'POST',
    '/api/tests',
    {
      folderPath: payload.folderPath,
    },
    {
      fileName: payload.fileName,
      folder: payload.folder || null,
      name: payload.name,
      description: payload.description,
      endpoint: payload.endpoint,
      method: payload.method,
      variables: payload.variables || {},
      steps: payload.steps || [],
    },
  );
});

ipcMain.handle('clone-test', async (_, payload) =>
  hostRequest(
    'POST',
    '/api/tests/clone',
    { folderPath: payload.folderPath },
    { fileName: payload.fileName, name: payload.name },
  ),
);

ipcMain.handle('move-test', async (_, payload) =>
  hostRequest(
    'POST',
    '/api/tests/move',
    { folderPath: payload.folderPath },
    { fileName: payload.fileName, folder: payload.folder || '' },
  ),
);

ipcMain.handle('rename-folder', async (_, payload) =>
  hostRequest(
    'POST',
    '/api/folders/rename',
    { folderPath: payload.folderPath },
    { folder: payload.folder, newFolder: payload.newFolder },
  ),
);

ipcMain.handle('delete-folder', async (_, payload) =>
  hostRequest(
    'POST',
    '/api/folders/delete',
    { folderPath: payload.folderPath },
    { folder: payload.folder },
  ),
);

// Shows a native context menu built from `items` ({ id, label, enabled } or { separator: true }) and
// resolves with the id of the chosen item, or null when the menu is dismissed.
ipcMain.handle(
  'show-context-menu',
  (event, items) =>
    new Promise((resolve) => {
      const menu = Menu.buildFromTemplate(
        items.map((item) =>
          item.separator
            ? { type: 'separator' }
            : { label: item.label, enabled: item.enabled !== false, click: () => resolve(item.id) },
        ),
      );
      // The close callback can fire before the click handler; let a click win.
      menu.popup({
        window: BrowserWindow.fromWebContents(event.sender),
        callback: () => setTimeout(() => resolve(null), 0),
      });
    }),
);

ipcMain.handle('delete-test-case', async (_, payload) => {
  return hostRequest('DELETE', `/api/tests/${encodeTestPath(payload.fileName)}`, {
    folderPath: payload.folderPath,
  });
});

app.on('before-quit', () => {
  if (hostProcess && !hostProcess.killed) {
    hostProcess.kill();
  }
});

function secretStorePath() {
  return path.join(app.getPath('userData'), 'local-secrets.json');
}

function readSecretStore() {
  try {
    return JSON.parse(fs.readFileSync(secretStorePath(), 'utf8'));
  } catch {
    return {};
  }
}

function writeSecretStore(store) {
  fs.writeFileSync(secretStorePath(), JSON.stringify(store), { mode: 0o600 });
}

function assertSecureStorage() {
  const weakBackend =
    typeof safeStorage.getSelectedStorageBackend === 'function' &&
    safeStorage.getSelectedStorageBackend() === 'basic_text';
  if (!safeStorage.isEncryptionAvailable() || weakBackend) {
    throw new Error('Secure storage is not available on this system.');
  }
}

function readLocalSecrets(folderPath) {
  const stored = readSecretStore()[folderPath] || {};
  const secrets = {};
  for (const [name, encrypted] of Object.entries(stored)) {
    try {
      secrets[name] = safeStorage.decryptString(Buffer.from(encrypted, 'base64'));
    } catch {
      // Unreadable entry (e.g. stored by another user account): skip it; the run reports it as missing.
    }
  }
  return secrets;
}

async function hostRequest(method, route, query, body) {
  const response = await hostFetch(method, route, query, body);
  const text = await response.text();
  return text ? safeJsonParse(text) : null;
}

// Sends an authenticated request to the host and throws its error message when it does not succeed.
async function hostFetch(method, route, query, body, signal) {
  const baseUrl = await ensureHostRunning();
  const params = new URLSearchParams(query || {});
  const url = params.toString() ? `${baseUrl}${route}?${params.toString()}` : `${baseUrl}${route}`;

  const response = await fetch(url, {
    method,
    headers: {
      authorization: `Bearer ${HOST_TOKEN}`,
      'content-type': 'application/json',
    },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  });

  if (!response.ok) {
    const text = await response.text();
    const message =
      formatHostError(text ? safeJsonParse(text) : null) ||
      text ||
      `${response.status} ${response.statusText}`;
    throw new Error(message);
  }

  return response;
}

// Parses a newline-delimited JSON stream, yielding each object as soon as its line is complete.
async function* readJsonLines(stream) {
  const decoder = new TextDecoder();
  let buffer = '';
  for await (const chunk of stream) {
    buffer += decoder.decode(chunk, { stream: true });
    let newline = buffer.indexOf('\n');
    while (newline >= 0) {
      const line = buffer.slice(0, newline).trim();
      buffer = buffer.slice(newline + 1);
      if (line) yield JSON.parse(line);
      newline = buffer.indexOf('\n');
    }
  }
  if (buffer.trim()) yield JSON.parse(buffer);
}

async function ensureHostRunning() {
  if (hostBaseUrl) {
    return hostBaseUrl;
  }

  if (!hostReadyPromise) {
    hostReadyPromise = startHostProcess();
  }

  return hostReadyPromise;
}

function startHostProcess() {
  return new Promise((resolve, reject) => {
    const workspaceRoot = path.resolve(__dirname, '..');
    const projectPath = path.join(workspaceRoot, 'backend/src/Axiom');
    const args = [
      'run',
      '--project',
      projectPath,
      '--no-restore',
      '--no-build',
      '--',
      'serve',
      '--port',
      '0',
    ];

    const child = spawn(resolveDotnetPath(), args, {
      cwd: workspaceRoot,
      shell: false,
      env: {
        ...process.env,
        DOTNET_NOLOGO: '1',
        AXIOM_HOST_TOKEN: HOST_TOKEN,
      },
    });

    hostProcess = child;
    let stderr = '';
    let startupTimeout = setTimeout(() => {
      startupTimeout = null;
      if (!hostBaseUrl) {
        cleanup();
        reject(new Error('Axiom host startup timed out.'));
      }
    }, 15000);

    const handleStdout = (chunk) => {
      const text = chunk.toString();
      const match = text.match(/AXIOM_HOST_READY\s+(\S+)/);
      if (!match) {
        return;
      }

      hostBaseUrl = match[1].replace(/\/$/, '');
      if (startupTimeout) {
        clearTimeout(startupTimeout);
        startupTimeout = null;
      }
      child.stdout.off('data', handleStdout);
      resolve(hostBaseUrl);
    };

    const cleanup = () => {
      if (startupTimeout) {
        clearTimeout(startupTimeout);
        startupTimeout = null;
      }
      hostBaseUrl = null;
      hostReadyPromise = null;
      hostProcess = null;
    };

    child.stdout.on('data', handleStdout);
    child.stderr.on('data', (chunk) => {
      stderr += chunk.toString();
    });

    child.on('error', (error) => {
      cleanup();
      reject(error);
    });

    child.on('close', (code) => {
      if (!hostBaseUrl) {
        const details = stderr.trim();
        cleanup();
        reject(new Error(details || `Axiom host exited with code ${code ?? -1}.`));
        return;
      }

      cleanup();
    });
  });
}

// A test's path relative to the tests folder (orders/create.test.yaml) as a URL path: each segment encoded, '/' kept.
function encodeTestPath(relativePath) {
  return String(relativePath).split('/').map(encodeURIComponent).join('/');
}

function safeJsonParse(text) {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function formatHostError(data) {
  if (!data) {
    return null;
  }

  const lines = [];
  if (data.message) {
    lines.push(data.message);
  }

  if (Array.isArray(data.fieldErrors)) {
    data.fieldErrors.forEach((item) => {
      lines.push(`- ${item.field}: ${item.message}`);
    });
  }

  return lines.join('\n');
}

function resolveDotnetPath() {
  const candidates = [
    '/usr/local/bin/dotnet',
    '/opt/homebrew/bin/dotnet',
    '/usr/bin/dotnet',
    'C:/Program Files/dotnet/dotnet.exe',
  ];

  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) {
      return candidate;
    }
  }

  return 'dotnet';
}
