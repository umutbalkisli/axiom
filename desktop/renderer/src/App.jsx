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
import { allFolders, folderOf, isInFolder, statusKey } from './testTree.js';
import PromptDialog from './components/PromptDialog.jsx';

const api = window.axiomApi;
const RECENT_KEY = 'axiom-recent';
// The collection that was open when the app last closed; cleared when the user closes it.
const LAST_OPEN_KEY = 'axiom-last-open';

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
      strict: false,
      caseSensitive: false,
      tolerance: '',
      operator: type === 'request' ? '==' : '>',
      expected: type === 'request' ? '200' : '0',
    },
  ],
});
const newIncludeStep = (index) => ({
  id: `include_${index}`,
  type: 'include',
  name: '',
  method: 'GET',
  url: '',
  queryParams: [],
  headers: [],
  body: '',
  connection: '',
  sql: '',
  save_as: '',
  ref: '',
  assertions: [],
});
const emptyShared = () => ({ name: '', description: '', run: 'each', variables: {}, steps: [] });
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
  const [editKind, setEditKind] = useState('test');
  const [shared, setShared] = useState([]);
  const [test, setTest] = useState(emptyTest);
  const [savedTest, setSavedTest] = useState(JSON.stringify(emptyTest()));
  const [report, setReport] = useState(null);
  const [rawOutput, setRawOutput] = useState('');
  const [running, setRunning] = useState(false);
  // While a run is going: how many tests it has and the results that have come in so far.
  const [progress, setProgress] = useState(null);
  const [toast, setToast] = useState(null);
  const [recent, setRecent] = useState(readRecent);
  // The one open dialog, if any (move to folder, rename folder).
  const [dialog, setDialog] = useState(null);
  // Group tests by 'folder' or 'endpoint' in the sidebar and the test list.
  const [grouping, setGrouping] = useState(
    () => localStorage.getItem('axiom-grouping') || 'folder',
  );
  // The latest outcome of every test that has run, by statusKey; a run of some tests updates only theirs.
  const [statuses, setStatuses] = useState({});
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
    localStorage.setItem('axiom-grouping', grouping);
  }, [grouping]);
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
    const [data, list, sharedList, localNames, providers] = await Promise.all([
      api.getCollection({ folderPath }),
      api.listTests({ folderPath }),
      api.listShared({ folderPath }),
      api.listLocalSecrets({ folderPath }),
      api.getSecretProviders(),
    ]);
    const state = toCollectionState(data);
    setCollection(state);
    setSavedCollection(JSON.stringify(state));
    setCollectionName(data?.name || '');
    setTests(list.tests || []);
    setShared(sharedList || []);
    setLocalSecretNames(localNames || []);
    setSecretProviders(providers || []);
  }, []);

  const remember = (folderPath) => {
    const next = [folderPath, ...readRecent().filter((item) => item !== folderPath)].slice(0, 6);
    localStorage.setItem(RECENT_KEY, JSON.stringify(next));
    localStorage.setItem(LAST_OPEN_KEY, folderPath);
    setRecent(next);
  };

  const showFolder = useCallback(
    async (folderPath, withCollection) => {
      setFolder(folderPath);
      setHasCollection(withCollection);
      setActiveFile(null);
      setReport(null);
      setStatuses({});
      setStatuses({});
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
    const last = localStorage.getItem(LAST_OPEN_KEY);
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
  const closeCollection = () => {
    localStorage.removeItem(LAST_OPEN_KEY);
    setFolder(null);
    setHasCollection(false);
    setCollection(toCollectionState(null));
    setSavedCollection(JSON.stringify(toCollectionState(null)));
    setCollectionName('');
    setTests([]);
    setShared([]);
    setLocalSecretNames([]);
    setActiveFile(null);
    setEditKind('test');
    setTest(emptyTest());
    setSavedTest(JSON.stringify(emptyTest()));
    setReport(null);
    setRawOutput('');
    setCollectionTab('tests');
    setView('collection');
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

  const refreshShared = async () => setShared(await api.listShared({ folderPath: folder }));

  const openShared = async (fileName) => {
    const data = await api.getShared({ folderPath: folder, fileName });
    if (!data) return;
    const loaded = {
      name: data.name || '',
      description: data.description || '',
      run: data.run || 'each',
      variables: {},
      steps: normalizeSteps(data.steps || []),
    };
    setEditKind('shared');
    setActiveFile(fileName);
    setBuilderKey((key) => key + 1);
    setTest(loaded);
    setSavedTest(JSON.stringify(loaded));
    setView('builder');
  };
  const newShared = () => {
    const fresh = emptyShared();
    setEditKind('shared');
    setActiveFile(null);
    setBuilderKey((key) => key + 1);
    setTest(fresh);
    setSavedTest(JSON.stringify(fresh));
    setView('builder');
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
    setEditKind('test');
    setActiveFile(fileName);
    setBuilderKey((key) => key + 1);
    setTest(loaded);
    setSavedTest(JSON.stringify(loaded));
    setView('builder');
  };
  // A new test, created in `inFolder` when given (a folder path; the builder lets the user change it).
  const newTest = (inFolder) => {
    const fresh = { ...emptyTest(), folder: typeof inFolder === 'string' ? inFolder : '' };
    setEditKind('test');
    setActiveFile(null);
    setBuilderKey((key) => key + 1);
    setTest(fresh);
    setSavedTest(JSON.stringify(fresh));
    setView('builder');
  };
  const saveShared = async () => {
    if (!test.name.trim()) return;
    try {
      const result = await api.saveShared({
        folderPath: folder,
        fileName: activeFile,
        name: test.name,
        description: test.description,
        run: test.run,
        steps: toYamlSteps(test.steps),
      });
      setActiveFile(result.fileName);
      setSavedTest(JSON.stringify(test));
      notify(t.saved);
      await refreshShared();
    } catch (error) {
      window.alert(error.message || t.failedSave);
    }
  };
  const saveTest = async () => {
    if (editKind === 'shared') return saveShared();
    if (!test.name.trim() || !test.endpoint.trim()) return;
    try {
      const result = await api.saveTestCase({
        folderPath: folder,
        // Editing an existing file keeps (or follows) its name; a new test gets a unique name from the host,
        // in the folder chosen in the builder.
        fileName: activeFile,
        folder: activeFile ? null : test.folder || '',
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
  const deleteShared = async () => {
    if (!activeFile || !window.confirm(`${t.deleteConfirm} ${activeFile}?`)) return;
    try {
      await api.deleteShared({ folderPath: folder, fileName: activeFile });
    } catch (error) {
      window.alert(error.message);
      return;
    }
    setActiveFile(null);
    setTest(emptyTest());
    setSavedTest(JSON.stringify(emptyTest()));
    setEditKind('test');
    setCollectionTab('shared');
    setView('collection');
    notify(t.deleted);
    await refreshShared();
  };
  const deleteTest = async () => {
    if (editKind === 'shared') return deleteShared();
    if (!activeFile || !window.confirm(`${t.deleteConfirm} ${activeFile}?`)) return;
    await api.deleteTestCase({ folderPath: folder, fileName: activeFile });
    setActiveFile(null);
    setTest(emptyTest());
    setSavedTest(JSON.stringify(emptyTest()));
    setView('collection');
    notify(t.deleted);
    await refreshTests();
  };

  // "Get user (copy)", or "(copy 2)", "(copy 3)"... when that name is taken.
  const copyName = (name) => {
    const taken = new Set(tests.map((item) => item.name));
    let candidate = t.copyName.replace('{name}', name);
    for (let n = 2; taken.has(candidate); n++) {
      candidate = t.copyNameN.replace('{name}', name).replace('{n}', n);
    }
    return candidate;
  };
  // Copies a test as it is (only its name changes) and opens the copy, ready to become a variation.
  const cloneTest = async (fileName) => {
    const source = tests.find((item) => item.fileName === fileName);
    try {
      const result = await api.cloneTest({
        folderPath: folder,
        fileName,
        name: copyName(source?.name || fileName),
      });
      await refreshTests();
      notify(t.cloned);
      await openTest(result.fileName);
    } catch (error) {
      window.alert(error.message);
    }
  };
  // Right-click menu of a test in the sidebar or the collection's test list.
  const testMenu = async (fileName) => {
    const choice = await api.showContextMenu([
      { id: 'open', label: t.openShort },
      { id: 'run', label: t.runTest, enabled: !running },
      { separator: true },
      { id: 'clone', label: t.cloneTest },
      { id: 'move', label: t.moveToFolder },
    ]);
    if (choice === 'open') guarded(() => openTest(fileName))();
    if (choice === 'run') run([fileName]);
    if (choice === 'clone') guarded(() => cloneTest(fileName))();
    if (choice === 'move') askMoveTest(fileName);
  };
  // Right-click menu of a folder.
  const folderMenu = async (path) => {
    const choice = await api.showContextMenu([
      { id: 'new', label: t.newTestHere },
      { id: 'run', label: t.runFolder, enabled: !running },
      { separator: true },
      { id: 'rename', label: t.renameFolder },
      { id: 'delete', label: t.deleteFolder },
    ]);
    if (choice === 'new') guarded(() => newTest(path))();
    if (choice === 'run') run([`${path}/`]);
    if (choice === 'rename') askRenameFolder(path);
    if (choice === 'delete') deleteFolder(path);
  };

  // Moves a test to another folder. The host moves the file as it is and never overwrites: a name
  // that is taken there gets the next free one.
  const moveTest = async (fileName, targetFolder) => {
    if (folderOf(fileName) === targetFolder) return;
    const result = await api.moveTest({ folderPath: folder, fileName, folder: targetFolder });
    if (activeFile === fileName) setActiveFile(result.fileName);
    await refreshTests();
    notify(t.moved.replace('{path}', result.fileName));
  };
  const dropTest = (fileName, targetFolder) =>
    moveTest(fileName, targetFolder).catch((error) => notify(error.message, 'danger'));
  const askMoveTest = (fileName) =>
    setDialog({
      title: t.moveToFolder,
      label: t.folderLabel,
      hint: t.folderHint,
      initialValue: folderOf(fileName),
      suggestions: allFolders(tests),
      confirmLabel: t.move,
      submit: (value) => moveTest(fileName, value),
    });
  const askRenameFolder = (path) =>
    setDialog({
      title: t.renameFolder,
      label: t.folderLabel,
      hint: t.renameFolderHint,
      initialValue: path,
      suggestions: [],
      confirmLabel: t.rename,
      submit: async (value) => {
        const result = await api.renameFolder({
          folderPath: folder,
          folder: path,
          newFolder: value,
        });
        if (activeFile && isInFolder(activeFile, path)) {
          setActiveFile(`${result.folder}/${activeFile.slice(path.length + 1)}`);
        }
        await refreshTests();
        notify(t.folderRenamed.replace('{path}', result.folder));
      },
    });
  const deleteFolder = async (path) => {
    const count = tests.filter((item) => isInFolder(item.fileName, path)).length;
    if (!window.confirm(t.deleteFolderConfirm.replace('{count}', count).replace('{path}', path)))
      return;
    try {
      await api.deleteFolder({ folderPath: folder, folder: path });
    } catch (error) {
      window.alert(error.message);
      return;
    }
    if (activeFile && isInFolder(activeFile, path) && view === 'builder') {
      setActiveFile(null);
      setTest(emptyTest());
      setSavedTest(JSON.stringify(emptyTest()));
      setView('collection');
    }
    await refreshTests();
    notify(t.deleted);
  };

  const environments = useMemo(() => environmentNames(collection.secrets), [collection.secrets]);
  const selectedEnvironment = environments.includes(environment) ? environment : '';

  // Runs the whole collection, or only `selected` (a list of test file names). Results stream in as tests finish.
  const run = async (selected) => {
    const only = Array.isArray(selected) && selected.length ? selected : null;
    if (dirty && !window.confirm(t.discardConfirm)) return;
    setView('run');
    setRunning(true);
    const startedAt = new Date().toISOString();
    const finished = [];
    let total = null;
    setProgress({ total: null, tests: [] });
    const stopListening = api.onRunProgress((item) => {
      if (item.type === 'started') {
        total = item.total;
        setProgress((current) => ({ ...current, total: item.total }));
      } else if (item.type === 'test') {
        finished.push(item.test);
        setProgress((current) => ({ ...current, tests: [...current.tests, item.test] }));
      }
    });
    try {
      const result = await api.runTests({
        folderPath: folder,
        environment: selectedEnvironment || null,
        tests: only,
      });
      if (result.cancelled) {
        // Keep what finished before the user cancelled.
        const partial = {
          testCases: [...finished].sort((a, b) => a.sourceFile.localeCompare(b.sourceFile)),
          startedAt,
          completedAt: new Date().toISOString(),
        };
        setRawOutput('');
        setReport({
          ...buildReport(partial, tests),
          cancelled: { done: finished.length, total: total ?? finished.length },
          scope: only,
          stamp: Date.now(),
        });
        return;
      }
      setRawOutput([result.stdout, result.stderr].filter(Boolean).join('\n').trim());
      setReport(
        result.result
          ? { ...buildReport(result.result, tests), scope: only, stamp: Date.now() }
          : { error: t.noOutput },
      );
    } catch (error) {
      setReport({ error: error.message, scope: only, stamp: Date.now() });
    } finally {
      stopListening();
      setRunning(false);
      setProgress(null);
    }
  };
  const cancelRun = () => api.cancelRun();
  const rerunFailed = () =>
    run(
      (report?.tests || [])
        .filter((item) => item.outcome !== 'passed')
        .map((item) => item.fileName),
    );
  // Runs the builder's steps up to `stepIndex` as they are now (saved or not) and returns what that step received.
  const previewStep = (stepIndex) =>
    api.previewStep({
      folderPath: folder,
      environment: selectedEnvironment || null,
      stepIndex,
      test: {
        fileName: activeFile,
        name: test.name || 'unsaved test',
        description: test.description,
        method: test.method,
        endpoint: test.endpoint,
        variables: test.variables || {},
        steps: toYamlSteps(test.steps),
      },
    });
  useEffect(() => {
    if (!report?.tests) return;
    setStatuses((current) => {
      const next = { ...current };
      report.tests.forEach((item) => {
        next[statusKey(item)] = { passed: 'pass', error: 'error' }[item.outcome] || 'fail';
      });
      return next;
    });
  }, [report]);
  // Keyed by path too, so lists can look a test up either way.
  const runStatus = useMemo(() => {
    const map = { ...statuses };
    tests.forEach((item) => {
      if (statuses[statusKey(item)]) map[item.fileName] = statuses[statusKey(item)];
    });
    return map;
  }, [statuses, tests]);

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
      steps: [
        ...current.steps,
        type === 'include'
          ? newIncludeStep(current.steps.length + 1)
          : newStep(type, current.steps.length + 1),
      ],
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
        {
          expression: 'status',
          aggregate: '',
          strict: false,
          caseSensitive: false,
          tolerance: '',
          operator: '==',
          expected: '200',
        },
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

  const failedCount = (report?.failed || 0) + (report?.errors || 0);
  const goToCollection = guarded(() => setView('collection'));
  const backFromBuilder = guarded(() => {
    setCollectionTab(editKind === 'shared' ? 'shared' : 'tests');
    setView('collection');
  });
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
        activeFile={editKind === 'test' ? activeFile : null}
        view={view}
        openFolder={guarded(openFolder)}
        openCollectionSetup={guarded(() => openSetup('empty'))}
        closeCollection={guarded(closeCollection)}
        openTest={(fileName) => guarded(() => openTest(fileName))()}
        testMenu={testMenu}
        folderMenu={folderMenu}
        dropTest={dropTest}
        grouping={grouping}
        setGrouping={setGrouping}
        newTest={guarded(() => newTest())}
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
                onClick={() => run()}
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
              runTest={(fileName) => run([fileName])}
              runFolder={(path) => run([`${path}/`])}
              testMenu={testMenu}
              folderMenu={folderMenu}
              grouping={grouping}
              setGrouping={setGrouping}
              running={running}
              newTest={() => newTest()}
              shared={shared}
              openShared={openShared}
              newShared={newShared}
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
              runTest={editKind === 'test' && activeFile ? () => run([activeFile]) : null}
              cloneTest={
                editKind === 'test' && activeFile ? guarded(() => cloneTest(activeFile)) : null
              }
              running={running}
              previewStep={previewStep}
              folders={allFolders(tests)}
              back={backFromBuilder}
              kind={editKind}
              sharedList={shared}
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
              progress={progress}
              rawOutput={rawOutput}
              run={() => run(report?.scope)}
              runAll={() => run()}
              rerunFailed={rerunFailed}
              cancelRun={cancelRun}
              environment={selectedEnvironment}
            />
          )}
        </main>
      </div>
      {dialog && <PromptDialog t={t} {...dialog} close={() => setDialog(null)} />}
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
