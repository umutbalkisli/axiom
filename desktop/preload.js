const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('axiomApi', {
  chooseFolder: () => ipcRenderer.invoke('choose-folder'),
  initCollection: (payload) => ipcRenderer.invoke('init-collection', payload),
  importOpenApi: (payload) => ipcRenderer.invoke('import-openapi', payload),
  runTests: (payload) => ipcRenderer.invoke('run-tests', payload),
  listTests: (payload) => ipcRenderer.invoke('list-tests', payload),
  getCollection: (payload) => ipcRenderer.invoke('get-collection', payload),
  saveCollectionVariables: (payload) => ipcRenderer.invoke('save-collection-variables', payload),
  saveCollectionSettings: (payload) => ipcRenderer.invoke('save-collection-settings', payload),
  getTestCase: (payload) => ipcRenderer.invoke('get-test-case', payload),
  saveTestCase: (payload) => ipcRenderer.invoke('save-test-case', payload),
  deleteTestCase: (payload) => ipcRenderer.invoke('delete-test-case', payload),
  getAggregations: () => ipcRenderer.invoke('get-aggregations'),
  getSecretProviders: () => ipcRenderer.invoke('get-secret-providers'),
  listLocalSecrets: (payload) => ipcRenderer.invoke('list-local-secrets', payload),
  setLocalSecret: (payload) => ipcRenderer.invoke('set-local-secret', payload),
  deleteLocalSecret: (payload) => ipcRenderer.invoke('delete-local-secret', payload),
});
