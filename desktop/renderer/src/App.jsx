import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Sidebar from './components/Sidebar.jsx';
import Collection from './components/Collection.jsx';
import Builder from './components/Builder.jsx';
import RunReport from './components/RunReport.jsx';
import CollectionSetup from './components/CollectionSetup.jsx';
import Welcome from './components/Welcome.jsx';
import Icon from './components/Icons.jsx';
import {
  buildReport,
  getLanguage,
  getTheme,
  normalizeSteps,
  toYamlSteps,
  translations,
} from './i18n.js';

const api = window.axiomApi;
const RECENT_KEY = 'axiom-recent';

const newStep = (type, index) => ({
  id: `${type}_${index}`,
  type,
  name: type === 'request' ? 'Send request' : 'Run query',
  method: 'GET',
  url: '{{base_url}}',
  queryParams: [],
  headers: [{ key: 'Content-Type', value: 'application/json' }],
  body: '',
  connection: '',
  sql: '',
  save_as: '',
  assertions: [
    {
      expression: type === 'request' ? 'status' : 'row_count',
      aggregate: '',
      operator: type === 'request' ? '==' : '>',
      expected: type === 'request' ? '200' : '0',
    },
  ],
});
const emptyTest = () => ({
  name: '',
  description: '',
  method: 'GET',
  endpoint: '',
  variables: {},
  steps: [],
});

function readRecent() {
  try {
    return JSON.parse(localStorage.getItem(RECENT_KEY)) || [];
  } catch {
    return [];
  }
}

export default function App() {
  const [language, setLanguage] = useState(getLanguage());
  const t = translations[language];
  const [theme, setTheme] = useState(getTheme());
  const [view, setView] = useState('collection');
  const [collectionTab, setCollectionTab] = useState('tests');
  const [setupMode, setSetupMode] = useState('empty');
  const [folder, setFolder] = useState(null);
  const [hasCollection, setHasCollection] = useState(false);
  const [collection, setCollection] = useState(toCollectionState(null));
  const [savedCollection, setSavedCollection] = useState(JSON.stringify(toCollectionState(null)));
  const [collectionName, setCollectionName] = useState('');
  const [environment, setEnvironment] = useState(
    () => localStorage.getItem('axiom-environment') || '',
  );
  const [secretProviders, setSecretProviders] = useState([]);
  const [aggregations, setAggregations] = useState([]);
  const [localSecretNames, setLocalSecretNames] = useState([]);
  const [tests, setTests] = useState([]);
  const [activeFile, setActiveFile] = useState(null);
  const [builderKey, setBuilderKey] = useState(0);
  const [test, setTest] = useState(emptyTest);
  const [savedTest, setSavedTest] = useState(JSON.stringify(emptyTest()));
  const [report, setReport] = useState(null);
  const [rawOutput, setRawOutput] = useState('');
  const [running, setRunning] = useState(false);
  const [toast, setToast] = useState(null);
  const [recent, setRecent] = useState(readRecent);
  const toastTimer = useRef(null);

  const notify = useCallback((text, tone = 'success') => {
    clearTimeout(toastTimer.current);
    setToast({ text, tone });
    toastTimer.current = setTimeout(() => setToast(null), 3200);
  }, []);

  const collectionDirty = hasCollection && JSON.stringify(collection) !== savedCollection;
  const testDirty = view === 'builder' && JSON.stringify(test) !== savedTest;
  const dirty = (view === 'collection' && collectionDirty) || testDirty;
  // Runs `action` unless the user declines to discard unsaved edits.
  const guarded = (action) => () => {
    if (dirty && !window.confirm(t.discardConfirm)) return;
    action();
  };

  useEffect(() => {
    localStorage.setItem('axiom-language', language);
    document.documentElement.lang = language;
  }, [language]);
  useEffect(() => {
    localStorage.setItem('axiom-environment', environment);
  }, [environment]);
  useEffect(() => {
    localStorage.setItem('axiom-theme', theme);
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const applyTheme = () => {
      const effective = theme === 'system' ? (media.matches ? 'dark' : 'light') : theme;
      document.documentElement.setAttribute('data-theme', effective);
    };
    applyTheme();
    if (theme === 'system') {
      media.addEventListener('change', applyTheme);
      return () => media.removeEventListener('change', applyTheme);
    }
    return undefined;
  }, [theme]);
  useEffect(() => {
    api
      .getAggregations()
      .then((names) => setAggregations(names || []))
      .catch(() => {});
  }, []);

  const loadCollection = useCallback(async (folderPath) => {
    const [data, list, localNames, providers] = await Promise.all([
      api.getCollection({ folderPath }),
      api.listTests({ folderPath }),
      api.listLocalSecrets({ folderPath }),
      api.getSecretProviders(),
    ]);
    const state = toCollectionState(data);
    setCollection(state);
    setSavedCollection(JSON.stringify(state));
    setCollectionName(data?.name || '');
    setTests(list.tests || []);
    setLocalSecretNames(localNames || []);
    setSecretProviders(providers || []);
  }, []);

  const remember = (folderPath) => {
    const next = [folderPath, ...readRecent().filter((item) => item !== folderPath)].slice(0, 6);
    localStorage.setItem(RECENT_KEY, JSON.stringify(next));
    setRecent(next);
  };

  const showFolder = useCallback(
    async (folderPath, withCollection) => {
      setFolder(folderPath);
      setHasCollection(withCollection);
      setActiveFile(null);
      setReport(null);
      setCollectionTab('tests');
      setView('collection');
      if (withCollection) {
        try {
          await loadCollection(folderPath);
          remember(folderPath);
        } catch (error) {
          notify(error.message, 'danger');
        }
      }
    },
    [loadCollection, notify],
  );

  // Reopen the last collection on launch.
  useEffect(() => {
    const [last] = readRecent();
    if (!last) return;
    api.checkFolder({ folderPath: last }).then((state) => {
      if (state.exists && state.hasCollection) showFolder(last, true);
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const openFolder = async () => {
    const result = await api.chooseFolder();
    if (!result) return;
    await showFolder(result.folderPath, result.hasCollection);
    if (result.hasCollection) notify(`${t.collectionLoaded}`);
  };
  const openRecent = async (path) => {
    const state = await api.checkFolder({ folderPath: path });
    if (!state.exists || !state.hasCollection) {
      const next = readRecent().filter((item) => item !== path);
      localStorage.setItem(RECENT_KEY, JSON.stringify(next));
      setRecent(next);
      notify(t.folderMissing, 'danger');
      return;
    }
    await showFolder(path, true);
  };
  const openSetup = (mode) => {
    setSetupMode(mode);
    setView('setup');
  };

  const chooseDestination = async () =>
    folder && !hasCollection ? { folderPath: folder } : api.chooseFolder();

  const createCollection = async (name) => {
    const destination = await chooseDestination();
    if (!destination) return;
    try {
      await api.initCollection({ folderPath: destination.folderPath, collectionName: name });
      await showFolder(destination.folderPath, true);
      notify(t.collectionCreated);
    } catch (error) {
      window.alert(error.message || t.failedCreate);
    }
  };
  const importOpenApi = async (name, specificationUrl) => {
    const destination = await chooseDestination();
    if (!destination) return;
    try {
      const result = await api.importOpenApi({
        folderPath: destination.folderPath,
        collectionName: name,
        specificationUrl,
      });
      await showFolder(destination.folderPath, true);
      notify(`${t.imported}: ${result.imported}`);
    } catch (error) {
      window.alert(error.message || t.importFailed);
    }
  };

  const refreshTests = async () => {
    const list = await api.listTests({ folderPath: folder });
    setTests(list.tests || []);
  };

  const openTest = async (fileName) => {
    const data = await api.getTestCase({ folderPath: folder, fileName });
    if (!data) return;
    const loaded = {
      name: data.name || '',
      description: data.description || '',
      method: data.method || 'GET',
      endpoint: data.endpoint || '',
      variables: data.variables || {},
      steps: normalizeSteps(data.steps || []),
    };
    setActiveFile(fileName);
    setBuilderKey((key) => key + 1);
    setTest(loaded);
    setSavedTest(JSON.stringify(loaded));
    setView('builder');
  };
  const newTest = () => {
    const fresh = emptyTest();
    setActiveFile(null);
    setBuilderKey((key) => key + 1);
    setTest(fresh);
    setSavedTest(JSON.stringify(fresh));
    setView('builder');
  };
  const saveTest = async () => {
    if (!test.name.trim() || !test.endpoint.trim()) return;
    try {
      const result = await api.saveTestCase({
        folderPath: folder,
        // Editing an existing file keeps (or follows) its name; a new test gets a unique name from the host.
        fileName: activeFile,
        name: test.name,
        description: test.description,
        method: test.method,
        endpoint: test.endpoint,
        variables: test.variables || {},
        steps: toYamlSteps(test.steps),
      });
      setActiveFile(result.fileName);
      setSavedTest(JSON.stringify(test));
      notify(t.saved);
      await refreshTests();
    } catch (error) {
      window.alert(error.message || t.failedSave);
    }
  };
  const deleteTest = async () => {
    if (!activeFile || !window.confirm(`${t.deleteConfirm} ${activeFile}?`)) return;
    await api.deleteTestCase({ folderPath: folder, fileName: activeFile });
    setActiveFile(null);
    setTest(emptyTest());
    setSavedTest(JSON.stringify(emptyTest()));
    setView('collection');
    notify(t.deleted);
    await refreshTests();
  };

  const environments = useMemo(() => environmentNames(collection.secrets), [collection.secrets]);
  const selectedEnvironment = environments.includes(environment) ? environment : '';

  const run = async () => {
    if (dirty && !window.confirm(t.discardConfirm)) return;
    setView('run');
    setRunning(true);
    try {
      const result = await api.runTests({
        folderPath: folder,
        environment: selectedEnvironment || null,
      });
      setRawOutput([result.stdout, result.stderr].filter(Boolean).join('\n').trim());
      setReport(
        result.result
          ? { ...buildReport(result.result, tests), stamp: Date.now() }
          : { error: t.noOutput },
      );
    } catch (error) {
      setReport({ error: error.message, stamp: Date.now() });
    } finally {
      setRunning(false);
    }
  };
  const runStatus = useMemo(() => {
    const map = {};
    (report?.tests || []).forEach((item) => {
      map[item.fileName] = item.passed ? 'pass' : 'fail';
    });
    return map;
  }, [report]);

  const saveSettings = async () => {
    try {
      await api.saveCollectionSettings({
        folderPath: folder,
        variables: collection.variables,
        connections: collection.connections,
        secrets: Object.fromEntries(
          Object.entries(collection.secrets).map(([name, reference]) => [
            name,
            withLocalKeys(name, reference),
          ]),
        ),
      });
      setSavedCollection(JSON.stringify(collection));
      notify(t.saved);
    } catch (error) {
      window.alert(error.message);
    }
  };
  const discardSettings = () => setCollection(JSON.parse(savedCollection));
  const saveLocalSecret = async (name, value) => {
    try {
      await api.setLocalSecret({ folderPath: folder, name, value });
      setLocalSecretNames((names) => [...new Set([...names, name])]);
      notify(t.secretStored);
    } catch (error) {
      window.alert(error.message);
    }
  };
  const deleteLocalSecret = async (name) => {
    await api.deleteLocalSecret({ folderPath: folder, name });
    setLocalSecretNames((names) => names.filter((n) => n !== name));
  };

  const updateStep = (index, patch) =>
    setTest((current) => ({
      ...current,
      steps: current.steps.map((step, i) => (i === index ? { ...step, ...patch } : step)),
    }));
  const addStep = (type) =>
    setTest((current) => ({
      ...current,
      steps: [...current.steps, newStep(type, current.steps.length + 1)],
    }));
  const removeStep = (index) =>
    setTest((current) => ({ ...current, steps: current.steps.filter((_, i) => i !== index) }));
  const moveStep = (index, direction) =>
    setTest((current) => {
      const target = index + direction;
      if (target < 0 || target >= current.steps.length) return current;
      const steps = [...current.steps];
      [steps[index], steps[target]] = [steps[target], steps[index]];
      return { ...current, steps };
    });
  const addAssertion = (index) =>
    updateStep(index, {
      assertions: [
        ...test.steps[index].assertions,
        { expression: 'status', aggregate: '', operator: '==', expected: '200' },
      ],
    });
  const updateAssertion = (stepIndex, assertionIndex, patch) =>
    updateStep(stepIndex, {
      assertions: test.steps[stepIndex].assertions.map((a, i) =>
        i === assertionIndex ? { ...a, ...patch } : a,
      ),
    });
  const removeAssertion = (stepIndex, assertionIndex) =>
    updateStep(stepIndex, {
      assertions: test.steps[stepIndex].assertions.filter((_, i) => i !== assertionIndex),
    });

  const failedCount = report?.failed || 0;
  const goToCollection = guarded(() => setView('collection'));
  const showWelcome = view !== 'setup' && !hasCollection;

  return (
    <div className="app-shell">
      <Sidebar
        t={t}
        language={language}
        setLanguage={setLanguage}
        theme={theme}
        setTheme={setTheme}
        folder={folder}
        collectionName={collectionName}
        hasCollection={hasCollection}
        tests={tests}
        runStatus={runStatus}
        failedCount={failedCount}
        activeFile={activeFile}
        view={view}
        openFolder={guarded(openFolder)}
        openCollectionSetup={guarded(() => openSetup('empty'))}
        openTest={(fileName) => guarded(() => openTest(fileName))()}
        newTest={guarded(newTest)}
        goToCollection={goToCollection}
        goToRun={guarded(() => setView('run'))}
      />
      <div className="main-col">
        <header className="topbar">
          <div className="topbar-title">
            <h1>{hasCollection ? collectionName || folder.split(/[\\/]/).pop() : 'Axiom'}</h1>
            <p className={hasCollection ? '' : 'hint-line'} title={folder || ''}>
              {hasCollection ? folder : t.chooseFolder}
            </p>
          </div>
          {hasCollection && (
            <div className="topbar-actions">
              {environments.length > 0 && (
                <label className="env-select" title={t.environment}>
                  <Icon name="layers" size={14} />
                  <select
                    aria-label={t.environment}
                    value={selectedEnvironment}
                    onChange={(event) => setEnvironment(event.target.value)}
                  >
                    <option value="">{t.environmentDefault}</option>
                    {environments.map((name) => (
                      <option key={name} value={name}>
                        {name}
                      </option>
                    ))}
                  </select>
                </label>
              )}
              <button
                type="button"
                className="btn btn-primary run-btn"
                disabled={running}
                onClick={run}
              >
                <Icon
                  name={running ? 'spinner' : 'play'}
                  size={14}
                  className={running ? 'spin' : ''}
                />
                {t.run}
              </button>
            </div>
          )}
        </header>
        <main className="content">
          {view === 'setup' && (
            <CollectionSetup
              key={setupMode}
              t={t}
              mode={setupMode}
              createNew={createCollection}
              importOpenApi={importOpenApi}
              cancel={() => setView('collection')}
            />
          )}
          {showWelcome && (
            <Welcome
              t={t}
              folder={folder}
              recent={recent}
              openFolder={openFolder}
              openRecent={openRecent}
              openCollectionSetup={() => openSetup('empty')}
              openImport={() => openSetup('import')}
            />
          )}
          {view === 'collection' && hasCollection && (
            <Collection
              t={t}
              collection={collection}
              setCollection={setCollection}
              tab={collectionTab}
              setTab={setCollectionTab}
              dirty={collectionDirty}
              save={saveSettings}
              discard={discardSettings}
              tests={tests}
              runStatus={runStatus}
              openTest={openTest}
              newTest={newTest}
              openImport={() => openSetup('import')}
              secretProviders={secretProviders}
              localSecretNames={localSecretNames}
              saveLocalSecret={saveLocalSecret}
              deleteLocalSecret={deleteLocalSecret}
              notify={notify}
            />
          )}
          {view === 'builder' && (
            <Builder
              key={builderKey}
              t={t}
              test={test}
              fileName={activeFile}
              setTest={setTest}
              dirty={testDirty}
              isNew={!activeFile}
              saveTest={saveTest}
              deleteTest={deleteTest}
              back={goToCollection}
              addStep={addStep}
              removeStep={removeStep}
              moveStep={moveStep}
              updateStep={updateStep}
              addAssertion={addAssertion}
              updateAssertion={updateAssertion}
              removeAssertion={removeAssertion}
              aggregations={aggregations}
              connectionNames={Object.keys(collection.connections || {})}
              variableNames={[
                ...Object.keys(collection.variables || {}),
                ...Object.keys(test.variables || {}),
              ].filter(Boolean)}
            />
          )}
          {view === 'run' && (
            <RunReport
              key={report?.stamp || 'none'}
              t={t}
              report={report}
              running={running}
              rawOutput={rawOutput}
              run={run}
              environment={selectedEnvironment}
            />
          )}
        </main>
      </div>
      {toast && (
        <div className={`toast ${toast.tone}`} role="status">
          <Icon name={toast.tone === 'danger' ? 'xCircle' : 'checkCircle'} size={16} />
          {toast.text}
        </div>
      )}
    </div>
  );
}

// Names of every environment that has an override on at least one secret.
function environmentNames(secrets) {
  const names = new Set();
  Object.values(secrets || {}).forEach((reference) =>
    Object.keys(reference.environments || {}).forEach((name) => name && names.add(name)),
  );
  return [...names];
}

// A local secret's value is stored under the secret's own name, so its key always follows the name.
function withLocalKeys(name, reference) {
  const localKey = (source) => (source.provider === 'local' ? { ...source, key: name } : source);
  const next = localKey(reference);
  if (!reference.environments) return next;
  return {
    ...next,
    environments: Object.fromEntries(
      Object.entries(reference.environments).map(([env, source]) => [env, localKey(source)]),
    ),
  };
}

function toCollectionState(data) {
  return {
    variables: data?.variables || {},
    connections: data?.connections || {},
    secrets: data?.secrets || {},
  };
}
