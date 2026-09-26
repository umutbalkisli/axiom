import React, { useMemo, useState } from 'react';
import { groupByEndpoint } from '../i18n.js';
import { MethodBadge, splitEndpointLabel } from './Badge.jsx';
import Icon from './Icons.jsx';

function Segmented({ value, onChange, options, label }) {
  return (
    <div className="segmented" role="group" aria-label={label}>
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          className={value === option.value ? 'active' : ''}
          title={option.title}
          aria-pressed={value === option.value}
          onClick={() => onChange(option.value)}
        >
          {option.icon ? <Icon name={option.icon} size={14} /> : option.label}
        </button>
      ))}
    </div>
  );
}

export default function Sidebar({
  t,
  language,
  setLanguage,
  theme,
  setTheme,
  folder,
  collectionName,
  hasCollection,
  tests,
  runStatus,
  failedCount,
  activeFile,
  view,
  openFolder,
  openCollectionSetup,
  closeCollection,
  openTest,
  newTest,
  goToCollection,
  goToRun,
}) {
  const [query, setQuery] = useState('');
  const filtered = useMemo(() => {
    const needle = query.trim().toLowerCase();
    if (!needle) return tests;
    return tests.filter((item) =>
      [item.name, item.endpoint, item.method, item.fileName]
        .filter(Boolean)
        .some((value) => String(value).toLowerCase().includes(needle)),
    );
  }, [tests, query]);
  const groups = useMemo(() => groupByEndpoint(filtered, t), [filtered, t]);

  return (
    <aside className="sidebar" aria-label="Application navigation">
      <div className="brand">
        <span className="brand-mark" />
        <span className="brand-name">Axiom</span>
      </div>

      <div className="workspace-card">
        {hasCollection ? (
          <>
            <div className="workspace-name" title={collectionName}>
              {collectionName || folder.split(/[\\/]/).pop()}
            </div>
            <div className="workspace-path" title={folder}>
              {folder}
            </div>
          </>
        ) : (
          <div className="workspace-name text-muted-2">{t.noCollection}</div>
        )}
        <div className="workspace-actions">
          <button type="button" className="btn btn-ghost btn-sm" onClick={openFolder}>
            <Icon name="folder" size={14} /> {t.openShort}
          </button>
          <button type="button" className="btn btn-ghost btn-sm" onClick={openCollectionSetup}>
            <Icon name="plus" size={14} /> {t.newShort}
          </button>
          {hasCollection && (
            <button
              type="button"
              className="btn btn-ghost btn-sm"
              title={t.closeCollection}
              onClick={closeCollection}
            >
              <Icon name="x" size={14} /> {t.closeShort}
            </button>
          )}
        </div>
      </div>

      {hasCollection && (
        <nav className="nav-list" aria-label={t.collection}>
          <button
            type="button"
            className={`nav-item ${view === 'collection' ? 'active' : ''}`}
            onClick={goToCollection}
          >
            <Icon name="layers" size={16} />
            <span>{t.collection}</span>
          </button>
          <button
            type="button"
            className={`nav-item ${view === 'run' ? 'active' : ''}`}
            onClick={goToRun}
          >
            <Icon name="chart" size={16} />
            <span>{t.results}</span>
            {failedCount > 0 && <span className="count-pill danger">{failedCount}</span>}
          </button>
        </nav>
      )}

      {hasCollection && (
        <>
          <div className="sidebar-section">
            <span>{t.cases}</span>
            <span className="count-pill">{tests.length}</span>
            <button
              type="button"
              className="btn-icon"
              title={t.newCase}
              aria-label={t.newCase}
              onClick={newTest}
            >
              <Icon name="plus" size={15} />
            </button>
          </div>
          {tests.length > 5 && (
            <label className="search-field">
              <Icon name="search" size={14} />
              <input
                value={query}
                placeholder={t.searchTests}
                onChange={(event) => setQuery(event.target.value)}
              />
            </label>
          )}
          <div className="test-tree">
            {groups.length ? (
              groups.map((group) => {
                const { method, path } = splitEndpointLabel(group.label);
                return (
                  <div key={group.label} className="tree-group">
                    <div className="tree-group-label">
                      {method ? <MethodBadge method={method} /> : null}
                      <span className="mono" title={path}>
                        {path}
                      </span>
                    </div>
                    {group.items.map((item) => (
                      <button
                        type="button"
                        key={item.fileName}
                        className={`tree-item ${
                          activeFile === item.fileName && view === 'builder' ? 'active' : ''
                        }`}
                        onClick={() => openTest(item.fileName)}
                      >
                        <span
                          className={`run-dot ${runStatus[item.fileName] || ''}`}
                          title={
                            runStatus[item.fileName] === 'pass'
                              ? t.passed
                              : runStatus[item.fileName] === 'fail'
                                ? t.failed
                                : runStatus[item.fileName] === 'error'
                                  ? t.couldNotEvaluate
                                  : t.notRun
                          }
                        />
                        <span className="tree-item-name">{item.name}</span>
                      </button>
                    ))}
                  </div>
                );
              })
            ) : (
              <p className="sidebar-empty">{tests.length ? t.noMatches : t.empty}</p>
            )}
          </div>
        </>
      )}

      <div className="sidebar-foot">
        <Segmented
          label={t.theme}
          value={theme}
          onChange={setTheme}
          options={[
            { value: 'light', icon: 'sun', title: t.themeLight },
            { value: 'system', icon: 'monitor', title: t.themeSystem },
            { value: 'dark', icon: 'moon', title: t.themeDark },
          ]}
        />
        <Segmented
          label={t.language}
          value={language}
          onChange={setLanguage}
          options={[
            { value: 'en', label: 'EN', title: 'English' },
            { value: 'tr', label: 'TR', title: 'Türkçe' },
          ]}
        />
      </div>
    </aside>
  );
}
