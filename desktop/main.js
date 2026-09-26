const { app, BrowserWindow, dialog, ipcMain, safeStorage } = require('electron');
const { spawn } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');

const HOST_PORT = 50743;
let hostProcess = null;
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

ipcMain.handle('run-tests', async (_, payload) => {
  const data = await hostRequest(
    'POST',
    '/api/run',
    { folderPath: payload.folderPath },
    {
      localSecrets: readLocalSecrets(payload.folderPath),
      environment: payload.environment || null,
    },
  );

  return {
    exitCode: data.exitCode,
    stdout: data.report || '',
    stderr: '',
    result: data.result || null,
  };
});

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
  return hostRequest('GET', `/api/tests/${encodeURIComponent(payload.fileName)}`, {
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
      name: payload.name,
      description: payload.description,
      endpoint: payload.endpoint,
      method: payload.method,
      variables: payload.variables || {},
      steps: payload.steps || [],
    },
  );
});

ipcMain.handle('delete-test-case', async (_, payload) => {
  return hostRequest('DELETE', `/api/tests/${encodeURIComponent(payload.fileName)}`, {
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
  const baseUrl = await ensureHostRunning();
  const params = new URLSearchParams(query || {});
  const url = params.toString() ? `${baseUrl}${route}?${params.toString()}` : `${baseUrl}${route}`;

  const response = await fetch(url, {
    method,
    headers: {
      'content-type': 'application/json',
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  const text = await response.text();
  const data = text ? safeJsonParse(text) : null;

  if (!response.ok) {
    const message = formatHostError(data) || text || `${response.status} ${response.statusText}`;
    throw new Error(message);
  }

  return data;
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
      String(HOST_PORT),
    ];

    const child = spawn(resolveDotnetPath(), args, {
      cwd: workspaceRoot,
      shell: false,
      env: {
        ...process.env,
        DOTNET_NOLOGO: '1',
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
