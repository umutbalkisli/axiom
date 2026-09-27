import React, { useMemo, useState } from 'react';
import { groupByEndpoint } from '../i18n.js';
import { groupByFolder, isInFolder } from '../testTree.js';
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

// Switches the test list between folders and endpoints.
export function GroupingToggle({ t, grouping, setGrouping }) {
  return (
    <div className="segmented compact" role="group" aria-label={t.groupBy}>
      {[
        ['folder', 'folder', t.groupByFolder],
        ['endpoint', 'globe', t.groupByEndpoint],
      ].map(([value, icon, title]) => (
        <button
          key={value}
          type="button"
          className={grouping === value ? 'active' : ''}
          title={title}
          aria-label={title}
          aria-pressed={grouping === value}
          onClick={() => setGrouping(value)}
        >
          <Icon name={icon} size={13} />
        </button>
      ))}
    </div>
  );
}

const TEST_DRAG_TYPE = 'application/x-axiom-test';

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
  testMenu,
  folderMenu,
  dropTest,
  grouping,
  setGrouping,
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
  const endpointGroups = useMemo(() => groupByEndpoint(filtered, t), [filtered, t]);
  const folderGroups = useMemo(() => groupByFolder(filtered), [filtered]);
  const searching = query.trim().length > 0;
  const [collapsed, setCollapsed] = useState(() => new Set());
  // The folder a dragged test is over ('' is the top level), to highlight it.
  const [dropTarget, setDropTarget] = useState(null);

  const toggleFolder = (path) =>
    setCollapsed((current) => {
      const next = new Set(current);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  // A folder is hidden while one of its parents is collapsed (never while searching).
  const hiddenByParent = (path) =>
    !searching && [...collapsed].some((parent) => parent !== path && isInFolder(path, parent));
  // Makes an element a place to drop a dragged test into `path`.
  const dropProps = (path) => ({
    onDragOver: (event) => {
      if (!event.dataTransfer.types.includes(TEST_DRAG_TYPE)) return;
      event.preventDefault();
      event.dataTransfer.dropEffect = 'move';
      setDropTarget(path);
    },
    onDragLeave: () => setDropTarget((current) => (current === path ? null : current)),
    onDrop: (event) => {
      const fileName = event.dataTransfer.getData(TEST_DRAG_TYPE);
      setDropTarget(null);
      if (!fileName) return;
      event.preventDefault();
      dropTest(fileName, path);
    },
  });

  // A test row (a render function, not a component, so rows keep their focus across updates).
  const testItem = (item, indent, draggable = false) => {
    const status = runStatus[item.fileName];
    return (
      <button
        key={item.fileName}
        type="button"
        className={`tree-item ${activeFile === item.fileName && view === 'builder' ? 'active' : ''}`}
        style={indent ? { paddingLeft: 8 + indent * 12 } : undefined}
        title={item.fileName}
        draggable={draggable}
        onDragStart={(event) => {
          event.dataTransfer.setData(TEST_DRAG_TYPE, item.fileName);
          event.dataTransfer.effectAllowed = 'move';
        }}
        onDragEnd={() => setDropTarget(null)}
        onClick={() => openTest(item.fileName)}
        onContextMenu={(event) => {
          event.preventDefault();
          testMenu(item.fileName);
        }}
      >
        <span
          className={`run-dot ${status || ''}`}
          title={
            status === 'pass'
              ? t.passed
              : status === 'fail'
                ? t.failed
                : status === 'error'
                  ? t.couldNotEvaluate
                  : t.notRun
          }
        />
        <span className="tree-item-name">{item.name}</span>
      </button>
    );
  };

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
          <div
            className={`sidebar-section ${dropTarget === '' ? 'drop-target' : ''}`}
            {...dropProps('')}
          >
            <span>{t.cases}</span>
            <span className="count-pill">{tests.length}</span>
            <GroupingToggle t={t} grouping={grouping} setGrouping={setGrouping} />
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
            {!filtered.length && (
              <p className="sidebar-empty">{tests.length ? t.noMatches : t.empty}</p>
            )}
            {filtered.length > 0 &&
              grouping === 'folder' &&
              folderGroups.map((group) => {
                if (group.folder && hiddenByParent(group.folder)) return null;
                const open = searching || !collapsed.has(group.folder);
                return (
                  <div key={group.folder || '(top)'} className="tree-group">
                    {group.folder && (
                      <button
                        type="button"
                        className={`tree-folder ${dropTarget === group.folder ? 'drop-target' : ''}`}
                        style={{ paddingLeft: 8 + (group.depth - 1) * 12 }}
                        aria-expanded={open}
                        title={group.folder}
                        onClick={() => toggleFolder(group.folder)}
                        onContextMenu={(event) => {
                          event.preventDefault();
                          folderMenu(group.folder);
                        }}
                        {...dropProps(group.folder)}
                      >
                        <Icon name={open ? 'chevronDown' : 'chevronRight'} size={12} />
                        <Icon name="folder" size={14} />
                        <span className="tree-item-name">{group.name}</span>
                        <span className="tree-count">{group.total}</span>
                      </button>
                    )}
                    {open && group.items.map((item) => testItem(item, group.depth, true))}
                  </div>
                );
              })}
            {filtered.length > 0 &&
              grouping === 'endpoint' &&
              endpointGroups.map((group) => {
                const { method, path } = splitEndpointLabel(group.label);
                return (
                  <div key={group.label} className="tree-group">
                    <div className="tree-group-label">
                      {method ? <MethodBadge method={method} /> : null}
                      <span className="mono" title={path}>
                        {path}
                      </span>
                    </div>
                    {group.items.map((item) => testItem(item, 0))}
                  </div>
                );
              })}
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
