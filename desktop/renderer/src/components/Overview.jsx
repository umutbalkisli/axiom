import React, { useState } from 'react';
import { groupByEndpoint, toVariableName } from '../i18n.js';
import { MethodBadge, splitEndpointLabel } from './Badge.jsx';

function entriesFor(collection, key) {
  return Object.entries(collection[key] || {});
}
// Rebuilds the map in place so renaming a key keeps the row's position.
function updateEntry(collection, setCollection, kind, oldKey, newKey, value) {
  const next = {};
  for (const [key, current] of Object.entries(collection[kind] || {})) {
    if (key === oldKey) next[newKey] = value;
    else next[key] = current;
  }
  setCollection({ ...collection, [kind]: next });
}
function removeEntry(collection, setCollection, kind, key) {
  const next = { ...collection[kind] };
  delete next[key];
  setCollection({ ...collection, [kind]: next });
}

// One provider + key editor, used for a secret's default source and for each environment override.
function SourceFields({ t, source, providers, onChange }) {
  const keyFormat = providers.find((p) => p.name === source.provider)?.keyFormat;
  return (
    <>
      <div className="col-md-2">
        <select
          className="form-select"
          value={source.provider}
          onChange={(event) => onChange({ ...source, provider: event.target.value })}
        >
          {providers.map((p) => (
            <option key={p.name} value={p.name}>
              {p.name}
            </option>
          ))}
        </select>
      </div>
      <div className="col">
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
      </div>
    </>
  );
}

function SecretRow({
  t,
  name,
  reference,
  providers,
  isStored,
  onRename,
  onChange,
  onRemove,
  onSaveValue,
}) {
  const [value, setValue] = useState('');
  const overrides = Object.entries(reference.environments || {});
  const usesLocal =
    reference.provider === 'local' || overrides.some(([, source]) => source.provider === 'local');
  const setOverrides = (next) =>
    onChange({ ...reference, environments: next.length ? Object.fromEntries(next) : undefined });
  return (
    <div className="mb-3">
      <div className="row g-2 mb-2">
        <div className="col-md-3">
          <input
            className="form-control font-monospace"
            placeholder={t.secretName}
            value={name}
            onChange={(event) => onRename(event.target.value)}
          />
        </div>
        <SourceFields
          t={t}
          source={reference}
          providers={providers}
          onChange={(next) => onChange({ ...reference, ...next })}
        />
        <div className="col-auto">
          <button className="btn btn-outline-danger" onClick={onRemove}>
            {t.remove}
          </button>
        </div>
      </div>
      {overrides.map(([environment, source], index) => (
        <div className="row g-2 mb-2 ps-4" key={index}>
          <div className="col-md-3">
            <input
              className="form-control font-monospace"
              placeholder={t.environmentName}
              value={environment}
              onChange={(event) =>
                setOverrides(
                  overrides.map((entry, i) => (i === index ? [event.target.value, source] : entry)),
                )
              }
            />
          </div>
          <SourceFields
            t={t}
            source={source}
            providers={providers}
            onChange={(next) =>
              setOverrides(overrides.map((entry, i) => (i === index ? [environment, next] : entry)))
            }
          />
          <div className="col-auto">
            <button
              className="btn btn-outline-danger"
              onClick={() => setOverrides(overrides.filter((_, i) => i !== index))}
            >
              {t.remove}
            </button>
          </div>
        </div>
      ))}
      <div className="ps-4 d-flex flex-wrap gap-2">
        <button
          className="btn btn-sm btn-link p-0"
          onClick={() =>
            setOverrides([...overrides, ['', { provider: providers[0]?.name || 'env', key: '' }]])
          }
        >
          {t.addEnvironment}
        </button>
      </div>
      {usesLocal && (
        <div className="input-group mt-2 ps-4">
          <input
            type="password"
            autoComplete="off"
            className="form-control font-monospace"
            placeholder={isStored ? `${t.secretStored} ✓` : t.secretValue}
            value={value}
            onChange={(event) => setValue(event.target.value)}
          />
          <button
            className="btn btn-outline-secondary"
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
  );
}

export default function Overview({
  t,
  collection,
  setCollection,
  hasCollection,
  saveSettings,
  secretProviders,
  localSecretNames,
  saveLocalSecret,
  deleteLocalSecret,
  newTest,
  tests,
  openTest,
}) {
  const variables = entriesFor(collection, 'variables');
  const connections = entriesFor(collection, 'connections');
  const groups = groupByEndpoint(tests, t);
  return (
    <section>
      <div className="d-flex justify-content-between align-items-start gap-3 mb-4">
        <div>
          <h2 className="h5 mb-1">{t.overviewTitle}</h2>
          <p className="text-secondary mb-0">{t.overviewSub}</p>
        </div>
        <button className="btn btn-primary" disabled={!hasCollection} onClick={newTest}>
          {t.newCase}
        </button>
      </div>
      <div className="d-grid gap-3">
        <section className="card">
          <div className="card-header d-flex justify-content-between align-items-center">
            <span>{t.globals}</span>
            <div className="d-flex gap-2">
              <button
                className="btn btn-sm btn-outline-secondary"
                disabled={!hasCollection}
                onClick={() =>
                  setCollection({ ...collection, variables: { ...collection.variables, '': '' } })
                }
              >
                {t.addVariable}
              </button>
              <button
                className="btn btn-sm btn-primary"
                disabled={!hasCollection}
                onClick={saveSettings}
              >
                {t.saveAll}
              </button>
            </div>
          </div>
          <div className="card-body">
            {variables.map(([key, value], index) => (
              <div className="row g-2 mb-2" key={index}>
                <div className="col">
                  <input
                    className="form-control font-monospace"
                    value={key}
                    onChange={(event) =>
                      updateEntry(
                        collection,
                        setCollection,
                        'variables',
                        key,
                        toVariableName(event.target.value),
                        value,
                      )
                    }
                  />
                </div>
                <div className="col">
                  <input
                    className="form-control font-monospace"
                    value={value}
                    onChange={(event) =>
                      updateEntry(
                        collection,
                        setCollection,
                        'variables',
                        key,
                        key,
                        event.target.value,
                      )
                    }
                  />
                </div>
                <div className="col-auto">
                  <button
                    className="btn btn-outline-danger"
                    onClick={() => removeEntry(collection, setCollection, 'variables', key)}
                  >
                    {t.remove}
                  </button>
                </div>
              </div>
            ))}
          </div>
        </section>
        <section className="card">
          <div className="card-header d-flex justify-content-between align-items-center">
            <span>{t.connections}</span>
            <button
              className="btn btn-sm btn-outline-secondary"
              disabled={!hasCollection}
              onClick={() =>
                setCollection({
                  ...collection,
                  connections: {
                    ...collection.connections,
                    '': { provider: 'sqlite', connection_string: '' },
                  },
                })
              }
            >
              {t.addConnection}
            </button>
          </div>
          <div className="card-body">
            {connections.map(([key, value], index) => (
              <div className="row g-2 mb-2" key={index}>
                <div className="col-md-3">
                  <input
                    className="form-control font-monospace"
                    value={key}
                    onChange={(event) =>
                      updateEntry(
                        collection,
                        setCollection,
                        'connections',
                        key,
                        event.target.value,
                        value,
                      )
                    }
                  />
                </div>
                <div className="col-md-2">
                  <select
                    className="form-select"
                    value={value.provider || 'sqlite'}
                    onChange={(event) =>
                      setCollection({
                        ...collection,
                        connections: {
                          ...collection.connections,
                          [key]: { ...value, provider: event.target.value },
                        },
                      })
                    }
                  >
                    <option value="sqlite">sqlite</option>
                    <option value="sqlserver">sqlserver</option>
                  </select>
                </div>
                <div className="col">
                  <input
                    className="form-control font-monospace"
                    value={value.connection_string || ''}
                    onChange={(event) =>
                      setCollection({
                        ...collection,
                        connections: {
                          ...collection.connections,
                          [key]: { ...value, connection_string: event.target.value },
                        },
                      })
                    }
                  />
                </div>
                <div className="col-auto">
                  <button
                    className="btn btn-outline-danger"
                    onClick={() => removeEntry(collection, setCollection, 'connections', key)}
                  >
                    {t.remove}
                  </button>
                </div>
              </div>
            ))}
          </div>
        </section>
        <section className="card">
          <div className="card-header d-flex justify-content-between align-items-center">
            <span>{t.secrets}</span>
            <button
              className="btn btn-sm btn-outline-secondary"
              disabled={!hasCollection}
              onClick={() =>
                setCollection({
                  ...collection,
                  secrets: {
                    ...collection.secrets,
                    '': { provider: secretProviders[0]?.name || 'env', key: '' },
                  },
                })
              }
            >
              {t.addSecret}
            </button>
          </div>
          <div className="card-body">
            <p className="text-secondary small mb-1">{t.secretsHint}</p>
            <p className="text-secondary small">{t.environmentHint}</p>
            {Object.entries(collection.secrets || {}).map(([name, reference], index) => (
              <SecretRow
                key={index}
                t={t}
                name={name}
                reference={reference}
                providers={secretProviders}
                isStored={localSecretNames.includes(name)}
                onRename={(newName) =>
                  updateEntry(collection, setCollection, 'secrets', name, newName, reference)
                }
                onChange={(next) =>
                  setCollection({
                    ...collection,
                    secrets: { ...collection.secrets, [name]: next },
                  })
                }
                onRemove={() => {
                  if (reference.provider === 'local' && name) deleteLocalSecret(name);
                  removeEntry(collection, setCollection, 'secrets', name);
                }}
                onSaveValue={(value) => saveLocalSecret(name, value)}
              />
            ))}
          </div>
        </section>
        <section className="card">
          <div className="card-header d-flex justify-content-between align-items-center">
            <span>{t.cases}</span>
            <span className="badge text-bg-light">{tests.length}</span>
          </div>
          <div className="list-group list-group-flush">
            {groups.length ? (
              groups.map((group) => {
                const { method, path } = splitEndpointLabel(group.label);
                return (
                  <React.Fragment key={group.label}>
                    <div className="list-group-item bg-transparent border-0 pt-3 pb-1 px-3">
                      <span className="axiom-group-label">
                        {method ? <MethodBadge method={method} /> : null}
                        <span className="axiom-mono">{path}</span>
                      </span>
                    </div>
                    {group.items.map((item) => (
                      <button
                        key={item.fileName}
                        className="list-group-item list-group-item-action d-flex justify-content-between align-items-center gap-3 px-3"
                        onClick={() => openTest(item.fileName)}
                      >
                        <span className="text-truncate">{item.name}</span>
                        <span className="axiom-mono">{item.fileName}</span>
                      </button>
                    ))}
                  </React.Fragment>
                );
              })
            ) : (
              <div className="list-group-item text-secondary">{t.empty}</div>
            )}
          </div>
        </section>
      </div>
    </section>
  );
}
