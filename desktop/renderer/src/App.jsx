import React, { useEffect, useState } from 'react';
import Sidebar from './components/Sidebar.jsx';
import Overview from './components/Overview.jsx';
import Builder from './components/Builder.jsx';
import RunReport from './components/RunReport.jsx';
import CollectionSetup from './components/CollectionSetup.jsx';
import {
  getLanguage,
  getTheme,
  normalizeSteps,
  parseReport,
  toYamlSteps,
  translations,
} from './i18n.js';

const api = window.axiomApi;
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
      operator: type === 'request' ? '==' : '>',
      expected: type === 'request' ? '200' : '0',
    },
  ],
});
const emptyTest = () => ({ name: '', description: '', method: 'GET', endpoint: '', steps: [] });
const titleFor = (folder, hasCollection, t) => {
  const name = folder.split(/[\\/]/).pop();
  const suffix = hasCollection ? t.collectionSuffix : `(${t.emptyFolder})`;
  return `${name} ${suffix}`;
};

export default function App() {
  const [language, setLanguage] = useState(getLanguage());
  const t = translations[language];
  const [theme, setTheme] = useState(getTheme());
  const [view, setView] = useState('overview');
  const [folder, setFolder] = useState(null);
  const [hasCollection, setHasCollection] = useState(false);
  const [collection, setCollection] = useState(toCollectionState(null));
  const [environment, setEnvironment] = useState(
    () => localStorage.getItem('axiom-environment') || '',
  );
  const [secretProviders, setSecretProviders] = useState([]);
  const [aggregations, setAggregations] = useState([]);
  const [localSecretNames, setLocalSecretNames] = useState([]);
  const [tests, setTests] = useState([]);
  const [activeFile, setActiveFile] = useState(null);
  const [test, setTest] = useState(emptyTest);
  const [report, setReport] = useState(null);
  const [rawOutput, setRawOutput] = useState('');
  const [rawOpen, setRawOpen] = useState(false);
  const [message, setMessage] = useState('');
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
  useEffect(() => {
    if (!folder || !hasCollection) return;
    Promise.all([
      api.getCollection({ folderPath: folder }),
      api.listTests({ folderPath: folder }),
      api.listLocalSecrets({ folderPath: folder }),
      api.getSecretProviders(),
    ]).then(([data, list, localNames, providers]) => {
      setCollection(toCollectionState(data));
      setTests(list.tests || []);
      setLocalSecretNames(localNames || []);
      setSecretProviders(providers || []);
    });
  }, [folder, hasCollection]);
  const openFolder = async () => {
    const result = await api.chooseFolder();
    if (!result) return;
    setFolder(result.folderPath);
    setHasCollection(result.hasCollection);
    setView('overview');
    setMessage(
      result.hasCollection
        ? `${t.collectionLoaded}: ${result.folderPath}`
        : `${t.folderSelected}: ${result.folderPath}. ${t.createToContinue}`,
    );
  };
  const createCollection = async (name) => {
    if (!name) return;
    const destination = await api.chooseFolder();
    if (!destination) return;
    try {
      await api.initCollection({
        folderPath: destination.folderPath,
        collectionName: name,
      });
      setFolder(destination.folderPath);
      setHasCollection(true);
      setMessage(`${t.collectionCreated}: ${destination.folderPath}`);
      setView('overview');
    } catch (error) {
      window.alert(error.message || t.failedCreate);
    }
  };
  const importOpenApi = async (name, specificationUrl) => {
    if (!name) return;
    if (!specificationUrl) return;
    const destination = await api.chooseFolder();
    if (!destination) return;
    try {
      const result = await api.importOpenApi({
        folderPath: destination.folderPath,
        collectionName: name,
        specificationUrl,
      });
      setFolder(destination.folderPath);
      setHasCollection(true);
      setMessage(`${t.imported}: ${result.imported}`);
      const list = await api.listTests({ folderPath: destination.folderPath });
      setTests(list.tests || []);
      const data = await api.getCollection({ folderPath: destination.folderPath });
      setCollection(toCollectionState(data));
      setView('overview');
    } catch (error) {
      window.alert(error.message || t.importFailed);
    }
  };
  const openTest = async (fileName) => {
    const data = await api.getTestCase({ folderPath: folder, fileName });
    if (!data) return;
    setActiveFile(fileName);
    setTest({
      name: data.name || '',
      description: data.description || '',
      method: data.method || 'GET',
      endpoint: data.endpoint || '',
      steps: normalizeSteps(data.steps || []),
    });
    setView('builder');
  };
  const saveTest = async () => {
    if (!test.name.trim()) return window.alert(t.nameRequired);
    if (!test.endpoint.trim()) return window.alert(t.endpointRequired);
    try {
      const result = await api.saveTestCase({
        folderPath: folder,
        fileName: activeFile?.replace(/\.test\.yaml$/, '') || slugify(test.name),
        name: test.name,
        description: test.description,
        method: test.method,
        endpoint: test.endpoint,
        variables: {},
        steps: toYamlSteps(test.steps),
      });
      setActiveFile(result.fileName);
      setMessage(`${t.saved}: ${result.fileName}`);
      const list = await api.listTests({ folderPath: folder });
      setTests(list.tests || []);
    } catch (error) {
      window.alert(error.message || t.failedSave);
    }
  };
  const deleteTest = async () => {
    if (!activeFile || !window.confirm(`${t.deleteConfirm} ${activeFile}?`)) return;
    await api.deleteTestCase({ folderPath: folder, fileName: activeFile });
    setActiveFile(null);
    setTest(emptyTest());
    setView('overview');
    setMessage(t.deleted);
    const list = await api.listTests({ folderPath: folder });
    setTests(list.tests || []);
  };
  const run = async () => {
    setView('run');
    setRawOutput(t.running);
    const result = await api.runTests({
      folderPath: folder,
      environment: environments.includes(environment) ? environment : null,
    });
    const output = [result.stdout, result.stderr].filter(Boolean).join('\n').trim();
    setRawOutput(output || t.noOutput);
    setReport(parseReport(output, tests, t));
  };
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
      setMessage(t.saved);
    } catch (error) {
      window.alert(error.message);
    }
  };
  const saveLocalSecret = async (name, value) => {
    try {
      await api.setLocalSecret({ folderPath: folder, name, value });
      setLocalSecretNames((names) => [...new Set([...names, name])]);
    } catch (error) {
      window.alert(error.message);
    }
  };
  const deleteLocalSecret = async (name) => {
    await api.deleteLocalSecret({ folderPath: folder, name });
    setLocalSecretNames((names) => names.filter((n) => n !== name));
  };
  const environments = environmentNames(collection.secrets);
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
  return (
    <div className="app-shell">
      <Sidebar
        t={t}
        language={language}
        setLanguage={setLanguage}
        theme={theme}
        setTheme={setTheme}
        folder={folder}
        hasCollection={hasCollection}
        tests={tests}
        activeFile={activeFile}
        view={view}
        openFolder={openFolder}
        openCollectionSetup={() => setView('setup')}
        openTest={openTest}
        goToOverview={() => setView('overview')}
      />
      <div className="axiom-main-col">
        <header className="axiom-topbar">
          <div>
            <h1 className="axiom-page-title">
              {folder ? titleFor(folder, hasCollection, t) : t.noCollection}
            </h1>
            <p className="axiom-status">{message || (folder ? t.chooseHint : t.chooseFolder)}</p>
          </div>
          <div className="d-flex align-items-center gap-2">
            {environments.length > 0 && (
              <select
                className="form-select"
                title={t.environment}
                aria-label={t.environment}
                value={environments.includes(environment) ? environment : ''}
                onChange={(event) => setEnvironment(event.target.value)}
              >
                <option value="">
                  {t.environment}: {t.environmentDefault}
                </option>
                {environments.map((name) => (
                  <option key={name} value={name}>
                    {t.environment}: {name}
                  </option>
                ))}
              </select>
            )}
            <button className="btn btn-primary" disabled={!hasCollection} onClick={run}>
              {t.run}
            </button>
          </div>
        </header>
        <main className="axiom-main">
          {view === 'setup' && (
            <CollectionSetup t={t} createNew={createCollection} importOpenApi={importOpenApi} />
          )}
          {view === 'overview' && (
            <Overview
              t={t}
              collection={collection}
              setCollection={setCollection}
              hasCollection={hasCollection}
              saveSettings={saveSettings}
              secretProviders={secretProviders}
              localSecretNames={localSecretNames}
              saveLocalSecret={saveLocalSecret}
              deleteLocalSecret={deleteLocalSecret}
              tests={tests}
              openTest={openTest}
              newTest={() => {
                setActiveFile(null);
                setTest(emptyTest());
                setView('builder');
              }}
            />
          )}
          {view === 'builder' && (
            <Builder
              t={t}
              test={test}
              setTest={setTest}
              saveTest={saveTest}
              deleteTest={deleteTest}
              addStep={addStep}
              removeStep={removeStep}
              updateStep={updateStep}
              addAssertion={addAssertion}
              updateAssertion={updateAssertion}
              removeAssertion={removeAssertion}
              aggregations={aggregations}
            />
          )}
          {view === 'run' && (
            <RunReport
              t={t}
              report={report}
              rawOutput={rawOutput}
              rawOpen={rawOpen}
              setRawOpen={setRawOpen}
            />
          )}
        </main>
      </div>
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

function slugify(value) {
  return (
    value
      .toLowerCase()
      .trim()
      .replace(/[^a-z0-9-_]+/g, '-')
      .replace(/-+/g, '-')
      .replace(/^-|-$/g, '') || 'new-test'
  );
}
