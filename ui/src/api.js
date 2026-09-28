import { getPref } from './preferences.js';

// Calls to the axiom program that serves this UI. The session cookie it set when the UI was opened
// authenticates every call (same origin, so the browser sends it by itself).

const GONE = {
  en: 'Axiom is no longer running. Start it again, then reload this page.',
  tr: 'Axiom artık çalışmıyor. Yeniden başlatın, sonra bu sayfayı yenileyin.',
};

async function send(method, route, query, body, signal) {
  const params = new URLSearchParams(query || {});
  const url = params.toString() ? `${route}?${params}` : route;
  let response;
  try {
    response = await fetch(url, {
      method,
      headers: body === undefined ? undefined : { 'content-type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
      signal,
    });
  } catch (error) {
    if (error.name === 'AbortError') throw error;
    throw new Error(GONE[getPref('language', 'en')] || GONE.en);
  }

  if (!response.ok) {
    const text = await response.text();
    throw new Error(
      describeError(parse(text)) || text || `${response.status} ${response.statusText}`,
    );
  }

  return response;
}

async function request(method, route, query, body) {
  const text = await (await send(method, route, query, body)).text();
  return text ? parse(text) : null;
}

function parse(text) {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function describeError(data) {
  if (!data) return null;
  const lines = [data.message || data.detail].filter(Boolean);
  (data.fieldErrors || []).forEach((item) => lines.push(`- ${item.field}: ${item.message}`));
  return lines.join('\n');
}

// A test's path relative to the tests folder (orders/create.test.yaml) as a URL path: each segment encoded.
const testPath = (fileName) => String(fileName).split('/').map(encodeURIComponent).join('/');

// Newline-delimited JSON, one object per complete line, as soon as it arrives.
async function* jsonLines(stream) {
  const reader = stream.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
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

let activeRun = null;
const progressListeners = new Set();

export const api = {
  // folders on this machine (for the folder picker)
  listDirectories: (path) => request('GET', '/api/fs/list', path ? { path } : {}),
  makeDirectory: (parent, name) => request('POST', '/api/fs/mkdir', {}, { parent, name }),
  checkFolder: ({ folderPath }) => request('GET', '/api/fs/check', { path: folderPath }),

  // collection
  initCollection: ({ folderPath, collectionName }) =>
    request('POST', '/api/collection/init', { folderPath }, { collectionName }),
  importOpenApi: ({ folderPath, collectionName, specificationUrl }) =>
    request(
      'POST',
      '/api/collection/import-openapi',
      { folderPath },
      { collectionName, specificationUrl },
    ),
  getCollection: ({ folderPath }) => request('GET', '/api/collection', { folderPath }),
  saveCollectionVariables: ({ folderPath, variables }) =>
    request('POST', '/api/collection/variables', { folderPath }, variables || {}),
  saveCollectionSettings: ({ folderPath, variables, connections, secrets }) =>
    request(
      'POST',
      '/api/collection/settings',
      { folderPath },
      { variables: variables || {}, connections: connections || {}, secrets: secrets || {} },
    ),
  getSecretProviders: async () => (await request('GET', '/api/secrets/providers'))?.providers || [],
  getAggregations: async () =>
    (await request('GET', '/api/assertions/aggregations'))?.aggregations || [],

  // tests
  listTests: ({ folderPath }) => request('GET', '/api/tests', { folderPath }),
  getTestCase: ({ folderPath, fileName }) =>
    request('GET', `/api/tests/${testPath(fileName)}`, { folderPath }),
  saveTestCase: ({ folderPath, ...test }) =>
    request(
      'POST',
      '/api/tests',
      { folderPath },
      {
        ...test,
        folder: test.folder || null,
        variables: test.variables || {},
        steps: test.steps || [],
      },
    ),
  deleteTestCase: ({ folderPath, fileName }) =>
    request('DELETE', `/api/tests/${testPath(fileName)}`, { folderPath }),
  cloneTest: ({ folderPath, fileName, name }) =>
    request('POST', '/api/tests/clone', { folderPath }, { fileName, name }),
  moveTest: ({ folderPath, fileName, folder }) =>
    request('POST', '/api/tests/move', { folderPath }, { fileName, folder: folder || '' }),
  renameFolder: ({ folderPath, folder, newFolder }) =>
    request('POST', '/api/folders/rename', { folderPath }, { folder, newFolder }),
  deleteFolder: ({ folderPath, folder }) =>
    request('POST', '/api/folders/delete', { folderPath }, { folder }),
  previewStep: ({ folderPath, test, stepIndex, environment }) =>
    request(
      'POST',
      '/api/tests/preview',
      { folderPath },
      { test, stepIndex, environment: environment || null },
    ),

  // shared steps
  listShared: async ({ folderPath }) =>
    (await request('GET', '/api/shared', { folderPath }))?.shared || [],
  getShared: ({ folderPath, fileName }) =>
    request('GET', `/api/shared/${encodeURIComponent(fileName)}`, { folderPath }),
  saveShared: ({ folderPath, fileName, name, description, run, steps }) =>
    request(
      'POST',
      '/api/shared',
      { folderPath },
      { fileName, name, description, run, steps: steps || [] },
    ),
  deleteShared: ({ folderPath, fileName }) =>
    request('DELETE', `/api/shared/${encodeURIComponent(fileName)}`, { folderPath }),

  // local secrets: values go into the operating system's secure storage and are never read back by the UI
  listLocalSecrets: async ({ folderPath }) =>
    (await request('GET', '/api/local-secrets', { folderPath }))?.names || [],
  setLocalSecret: ({ folderPath, name, value }) =>
    request('POST', '/api/local-secrets', { folderPath }, { name, value }),
  deleteLocalSecret: ({ folderPath, name }) =>
    request('DELETE', `/api/local-secrets/${encodeURIComponent(name)}`, { folderPath }),

  // Runs the collection (or `tests`), passing each progress event to onRunProgress listeners.
  // Resolves with the final result, or { cancelled: true } after cancelRun.
  runTests: async ({ folderPath, environment, tests }) => {
    activeRun?.abort();
    const controller = new AbortController();
    activeRun = controller;
    let completed = null;
    try {
      const response = await send(
        'POST',
        '/api/run',
        { folderPath },
        { environment: environment || null, tests: tests?.length ? tests : null },
        controller.signal,
      );
      for await (const item of jsonLines(response.body)) {
        if (item.type === 'failed') throw new Error(item.message);
        if (item.type === 'completed') completed = item;
        else progressListeners.forEach((listener) => listener(item));
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
  },
  // Closing the request cancels the run on the host.
  cancelRun: async () => activeRun?.abort(),
  onRunProgress: (listener) => {
    progressListeners.add(listener);
    return () => progressListeners.delete(listener);
  },
};
