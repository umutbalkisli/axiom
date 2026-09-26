import React, { useState } from 'react';
import { groupByEndpoint, toVariableName } from '../i18n.js';
import { MethodBadge, splitEndpointLabel } from './Badge.jsx';
import EmptyState from './EmptyState.jsx';
import Icon from './Icons.jsx';

// Rebuilds the map in place so renaming a key keeps the row's position.
function renameEntry(map, oldKey, newKey, value) {
  const next = {};
  for (const [key, current] of Object.entries(map || {})) {
    if (key === oldKey) next[newKey] = value;
    else next[key] = current;
  }
  return next;
}

function withoutKey(map, removedKey) {
  return Object.fromEntries(Object.entries(map || {}).filter(([key]) => key !== removedKey));
}

function Tabs({ tabs, active, onChange }) {
  return (
    <div className="tabs" role="tablist">
      {tabs.map((tab) => (
        <button
          key={tab.id}
          type="button"
          role="tab"
          aria-selected={active === tab.id}
          className={`tab ${active === tab.id ? 'active' : ''}`}
          onClick={() => onChange(tab.id)}
        >
          <Icon name={tab.icon} size={15} />
          {tab.label}
          <span className="count-pill">{tab.count}</span>
        </button>
      ))}
    </div>
  );
}

function TestsTab({ t, tests, runStatus, openTest, newTest, openImport }) {
  const groups = groupByEndpoint(tests, t);
  if (!tests.length) {
    return (
      <EmptyState
        icon="list"
        title={t.noTestsTitle}
        description={t.noTestsDescription}
        action={
          <div className="empty-actions">
            <button type="button" className="btn btn-primary" onClick={newTest}>
              <Icon name="plus" size={15} /> {t.newCase}
            </button>
            <button type="button" className="btn btn-secondary" onClick={openImport}>
              <Icon name="download" size={15} /> {t.importOpenApi}
            </button>
          </div>
        }
      />
    );
  }
  return (
    <div className="stack">
      {groups.map((group) => {
        const { method, path } = splitEndpointLabel(group.label);
        return (
          <section key={group.label}>
            <div className="group-label">
              {method ? <MethodBadge method={method} /> : null}
              <span className="mono">{path}</span>
            </div>
            <div className="list-card">
              {group.items.map((item) => (
                <button
                  type="button"
                  key={item.fileName}
                  className="list-row"
                  onClick={() => openTest(item.fileName)}
                >
                  <span className={`run-dot ${runStatus[item.fileName] || ''}`} />
                  <span className="list-row-main">
                    <span className="list-row-title">{item.name}</span>
                    <span className="mono muted">{item.fileName}</span>
                  </span>
                  <Icon name="chevronRight" size={14} />
                </button>
              ))}
            </div>
          </section>
        );
      })}
    </div>
  );
}

function SharedTab({ t, shared, openShared, newShared }) {
  if (!shared.length) {
    return (
      <EmptyState
        icon="swap"
        title={t.noSharedTitle}
        description={t.noSharedDescription}
        action={
          <button type="button" className="btn btn-primary" onClick={newShared}>
            <Icon name="plus" size={15} /> {t.newShared}
          </button>
        }
      />
    );
  }
  return (
    <div className="stack">
      <p className="hint">{t.sharedHint}</p>
      <div className="list-card">
        {shared.map((item) => (
          <button
            type="button"
            key={item.fileName}
            className="list-row"
            onClick={() => openShared(item.fileName)}
          >
            <Icon name="swap" size={16} />
            <span className="list-row-main">
              <span className="list-row-title">{item.name}</span>
              <span className="mono muted">{item.description || `shared/${item.fileName}`}</span>
            </span>
            <span className="chip">{item.run === 'once' ? t.runsOnce : t.runsEach}</span>
            <span className="chip">
              {item.stepCount} {item.stepCount === 1 ? t.stepOne : t.stepsShort}
            </span>
            <Icon name="chevronRight" size={14} />
          </button>
        ))}
      </div>
      <div>
        <button type="button" className="btn btn-secondary" onClick={newShared}>
          <Icon name="plus" size={15} /> {t.newShared}
        </button>
      </div>
    </div>
  );
}

function VariablesTab({ t, collection, setCollection }) {
  const entries = Object.entries(collection.variables || {});
  const set = (variables) => setCollection({ ...collection, variables });
  return (
    <div className="stack">
      <p className="hint">{t.variablesHint}</p>
      {entries.length === 0 ? (
        <EmptyState
          icon="variable"
          title={t.noVariablesTitle}
          description={t.noVariablesDescription}
        />
      ) : (
        <div className="panel">
          <div className="grid-head variables-grid">
            <span>{t.name}</span>
            <span>{t.value}</span>
            <span />
          </div>
          {entries.map(([key, value], index) => (
            <div className="grid-row variables-grid" key={index}>
              <input
                className="form-control font-monospace"
                value={key}
                placeholder="base_url"
                aria-label={t.name}
                onChange={(event) =>
                  set(
                    renameEntry(
                      collection.variables,
                      key,
                      toVariableName(event.target.value),
                      value,
                    ),
                  )
                }
              />
              <input
                className="form-control font-monospace"
                value={value}
                placeholder="https://api.example.com"
                aria-label={t.value}
                onChange={(event) =>
                  set(renameEntry(collection.variables, key, key, event.target.value))
                }
              />
              <button
                type="button"
                className="btn-icon danger"
                title={t.remove}
                aria-label={t.remove}
                onClick={() => set(withoutKey(collection.variables, key))}
              >
                <Icon name="trash" size={15} />
              </button>
            </div>
          ))}
        </div>
      )}
      <div>
        <button
          type="button"
          className="btn btn-secondary"
          onClick={() => set({ ...collection.variables, '': '' })}
        >
          <Icon name="plus" size={15} /> {t.addVariable}
        </button>
      </div>
    </div>
  );
}

function ConnectionsTab({ t, collection, setCollection }) {
  const entries = Object.entries(collection.connections || {});
  const set = (connections) => setCollection({ ...collection, connections });
  return (
    <div className="stack">
      <p className="hint">{t.connectionsHint}</p>
      {entries.length === 0 ? (
        <EmptyState
          icon="database"
          title={t.noConnectionsTitle}
          description={t.noConnectionsDescription}
        />
      ) : (
        entries.map(([key, value], index) => (
          <div className="panel" key={index}>
            <div className="panel-body connection-grid">
              <label className="field">
                <span className="field-label">{t.name}</span>
                <input
                  className="form-control font-monospace"
                  value={key}
                  placeholder="orders_db"
                  onChange={(event) =>
                    set(renameEntry(collection.connections, key, event.target.value, value))
                  }
                />
              </label>
              <label className="field">
                <span className="field-label">{t.provider}</span>
                <select
                  className="form-select"
                  value={value.provider || 'sqlite'}
                  onChange={(event) =>
                    set({
                      ...collection.connections,
                      [key]: { ...value, provider: event.target.value },
                    })
                  }
                >
                  <option value="sqlite">SQLite</option>
                  <option value="sqlserver">SQL Server</option>
                </select>
              </label>
              <button
                type="button"
                className="btn-icon danger align-end"
                title={t.remove}
                aria-label={t.remove}
                onClick={() => set(withoutKey(collection.connections, key))}
              >
                <Icon name="trash" size={15} />
              </button>
              <label className="field span-all">
                <span className="field-label">{t.connectionString}</span>
                <input
                  className="form-control font-monospace"
                  value={value.connection_string || ''}
                  placeholder="Server=db;Database=app;User Id=me;Password={{secret.db_password}}"
                  onChange={(event) =>
                    set({
                      ...collection.connections,
                      [key]: { ...value, connection_string: event.target.value },
                    })
                  }
                />
              </label>
            </div>
          </div>
        ))
      )}
      <div>
        <button
          type="button"
          className="btn btn-secondary"
          onClick={() =>
            set({ ...collection.connections, '': { provider: 'sqlite', connection_string: '' } })
          }
        >
          <Icon name="plus" size={15} /> {t.addConnection}
        </button>
      </div>
    </div>
  );
}

// One provider + key editor, used for a secret's default source and for each environment override.
function SourceFields({ t, source, providers, onChange }) {
  const keyFormat = providers.find((p) => p.name === source.provider)?.keyFormat;
  return (
    <>
      <select
        className="form-select"
        aria-label={t.provider}
        value={source.provider}
        onChange={(event) => onChange({ ...source, provider: event.target.value })}
      >
        {providers.map((p) => (
          <option key={p.name} value={p.name}>
            {p.name}
          </option>
        ))}
      </select>
      {source.provider === 'local' ? (
        <input className="form-control" disabled value={t.secretLocalNote} />
      ) : (
        <input
          className="form-control font-monospace"
          placeholder={keyFormat || t.secretKeyPlaceholder}
          value={source.key || ''}
          onChange={(event) => onChange({ ...source, key: event.target.value })}
        />
      )}
    </>
  );
}

function SecretCard({
  t,
  name,
  reference,
  providers,
  isStored,
  onRename,
  onChange,
  onRemove,
  onSaveValue,
  onCopy,
}) {
  const [value, setValue] = useState('');
  const overrides = Object.entries(reference.environments || {});
  const usesLocal =
    reference.provider === 'local' || overrides.some(([, source]) => source.provider === 'local');
  const setOverrides = (next) =>
    onChange({ ...reference, environments: next.length ? Object.fromEntries(next) : undefined });
  return (
    <div className="panel">
      <div className="panel-body secret-card">
        <div className="secret-head">
          <input
            className="form-control font-monospace"
            placeholder={t.secretName}
            aria-label={t.secretName}
            value={name}
            onChange={(event) => onRename(event.target.value)}
          />
          {name && (
            <button
              type="button"
              className="chip clickable"
              title={t.copySnippet}
              onClick={() => onCopy(`{{secret.${name}}}`)}
            >
              <span className="mono">{`{{secret.${name}}}`}</span>
              <Icon name="copy" size={13} />
            </button>
          )}
          <button
            type="button"
            className="btn-icon danger"
            title={t.remove}
            aria-label={t.remove}
            onClick={onRemove}
          >
            <Icon name="trash" size={15} />
          </button>
        </div>

        <div className="source-row">
          <span className="source-label">{t.secretDefaultSource}</span>
          <SourceFields
            t={t}
            source={reference}
            providers={providers}
            onChange={(next) => onChange({ ...reference, ...next })}
          />
        </div>

        {overrides.map(([environment, source], index) => (
          <div className="source-row override" key={index}>
            <input
              className="form-control font-monospace"
              placeholder={t.environmentName}
              aria-label={t.environment}
              value={environment}
              onChange={(event) =>
                setOverrides(
                  overrides.map((entry, i) => (i === index ? [event.target.value, source] : entry)),
                )
              }
            />
            <SourceFields
              t={t}
              source={source}
              providers={providers}
              onChange={(next) =>
                setOverrides(
                  overrides.map((entry, i) => (i === index ? [environment, next] : entry)),
                )
              }
            />
            <button
              type="button"
              className="btn-icon danger"
              title={t.remove}
              aria-label={t.remove}
              onClick={() => setOverrides(overrides.filter((_, i) => i !== index))}
            >
              <Icon name="x" size={14} />
            </button>
          </div>
        ))}

        <div className="secret-foot">
          <button
            type="button"
            className="btn btn-link"
            onClick={() =>
              setOverrides([...overrides, ['', { provider: providers[0]?.name || 'env', key: '' }]])
            }
          >
            <Icon name="plus" size={13} /> {t.addEnvironment}
          </button>
        </div>

        {usesLocal && (
          <div className="local-value">
            <Icon name="key" size={15} />
            <input
              type="password"
              autoComplete="off"
              className="form-control font-monospace"
              placeholder={isStored ? `${t.secretStored} ✓` : t.secretValue}
              value={value}
              onChange={(event) => setValue(event.target.value)}
            />
            <button
              type="button"
              className="btn btn-secondary"
              disabled={!name || !value}
              onClick={() => {
                onSaveValue(value);
                setValue('');
              }}
            >
              {t.secretSetValue}
            </button>
          </div>
        )}
      </div>
    </div>
  );
}

function SecretsTab({
  t,
  collection,
  setCollection,
  secretProviders,
  localSecretNames,
  saveLocalSecret,
  deleteLocalSecret,
  notify,
}) {
  const entries = Object.entries(collection.secrets || {});
  const set = (secrets) => setCollection({ ...collection, secrets });
  return (
    <div className="stack">
      <div className="notice info">
        <Icon name="key" size={16} />
        <div>
          <div>{t.secretsHint}</div>
          <div className="muted">{t.environmentHint}</div>
        </div>
      </div>
      {entries.length === 0 ? (
        <EmptyState icon="key" title={t.noSecretsTitle} description={t.noSecretsDescription} />
      ) : (
        entries.map(([name, reference], index) => (
          <SecretCard
            key={index}
            t={t}
            name={name}
            reference={reference}
            providers={secretProviders}
            isStored={localSecretNames.includes(name)}
            onRename={(newName) => set(renameEntry(collection.secrets, name, newName, reference))}
            onChange={(next) => set({ ...collection.secrets, [name]: next })}
            onRemove={() => {
              if (reference.provider === 'local' && name) deleteLocalSecret(name);
              set(withoutKey(collection.secrets, name));
            }}
            onSaveValue={(value) => saveLocalSecret(name, value)}
            onCopy={(text) => {
              navigator.clipboard?.writeText(text);
              notify(t.copied);
            }}
          />
        ))
      )}
      <div>
        <button
          type="button"
          className="btn btn-secondary"
          onClick={() =>
            set({
              ...collection.secrets,
              '': { provider: secretProviders[0]?.name || 'env', key: '' },
            })
          }
        >
          <Icon name="plus" size={15} /> {t.addSecret}
        </button>
      </div>
    </div>
  );
}

export default function Collection({
  t,
  collection,
  setCollection,
  tab,
  setTab,
  dirty,
  save,
  discard,
  tests,
  runStatus,
  openTest,
  newTest,
  shared,
  openShared,
  newShared,
  openImport,
  secretProviders,
  localSecretNames,
  saveLocalSecret,
  deleteLocalSecret,
  notify,
}) {
  const tabs = [
    { id: 'tests', label: t.cases, icon: 'list', count: tests.length },
    { id: 'shared', label: t.sharedTab, icon: 'swap', count: shared.length },
    {
      id: 'variables',
      label: t.globals,
      icon: 'variable',
      count: Object.keys(collection.variables || {}).length,
    },
    {
      id: 'connections',
      label: t.connections,
      icon: 'database',
      count: Object.keys(collection.connections || {}).length,
    },
    {
      id: 'secrets',
      label: t.secrets,
      icon: 'key',
      count: Object.keys(collection.secrets || {}).length,
    },
  ];
  return (
    <div className="page">
      <header className="page-header">
        <div>
          <h2>{t.overviewTitle}</h2>
          <p>{t.overviewSub}</p>
        </div>
        <button type="button" className="btn btn-primary" onClick={newTest}>
          <Icon name="plus" size={15} /> {t.newCase}
        </button>
      </header>

      <Tabs tabs={tabs} active={tab} onChange={setTab} />

      <div className="tab-panel">
        {tab === 'tests' && (
          <TestsTab
            t={t}
            tests={tests}
            runStatus={runStatus}
            openTest={openTest}
            newTest={newTest}
            openImport={openImport}
          />
        )}
        {tab === 'shared' && (
          <SharedTab t={t} shared={shared} openShared={openShared} newShared={newShared} />
        )}
        {tab === 'variables' && (
          <VariablesTab t={t} collection={collection} setCollection={setCollection} />
        )}
        {tab === 'connections' && (
          <ConnectionsTab t={t} collection={collection} setCollection={setCollection} />
        )}
        {tab === 'secrets' && (
          <SecretsTab
            t={t}
            collection={collection}
            setCollection={setCollection}
            secretProviders={secretProviders}
            localSecretNames={localSecretNames}
            saveLocalSecret={saveLocalSecret}
            deleteLocalSecret={deleteLocalSecret}
            notify={notify}
          />
        )}
      </div>

      {dirty && (
        <div className="save-bar" role="status">
          <span className="dirty-dot" />
          <span>{t.unsavedChanges}</span>
          <span className="spacer" />
          <button type="button" className="btn btn-ghost" onClick={discard}>
            {t.discard}
          </button>
          <button type="button" className="btn btn-primary" onClick={save}>
            {t.saveChanges}
          </button>
        </div>
      )}
    </div>
  );
}
