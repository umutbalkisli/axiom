import React from 'react';
import { groupByEndpoint } from '../i18n.js';
import { MethodBadge, splitEndpointLabel } from './Badge.jsx';

function entriesFor(collection, key) {
  return Object.entries(collection[key] || {});
}
function updateEntry(collection, setCollection, kind, oldKey, newKey, value) {
  const next = { ...collection[kind] };
  delete next[oldKey];
  next[newKey] = value;
  setCollection({ ...collection, [kind]: next });
}
function removeEntry(collection, setCollection, kind, key) {
  const next = { ...collection[kind] };
  delete next[key];
  setCollection({ ...collection, [kind]: next });
}

export default function Overview({
  t,
  collection,
  setCollection,
  hasCollection,
  saveSettings,
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
              <div className="row g-2 mb-2" key={`${key}-${index}`}>
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
                        event.target.value,
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
              <div className="row g-2 mb-2" key={`${key}-${index}`}>
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
