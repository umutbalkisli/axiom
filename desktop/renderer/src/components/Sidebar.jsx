import React, { useMemo } from 'react';
import { groupByEndpoint } from '../i18n.js';
import { MethodBadge, splitEndpointLabel } from './Badge.jsx';

export default function Sidebar({
  t,
  language,
  setLanguage,
  theme,
  setTheme,
  folder,
  hasCollection,
  tests,
  activeFile,
  view,
  openFolder,
  openCollectionSetup,
  openTest,
  goToOverview,
}) {
  const groups = useMemo(() => groupByEndpoint(tests, t), [tests, t]);

  return (
    <aside className="axiom-sidebar" aria-label="Application navigation">
      <div className="axiom-brand">
        <span className="axiom-brand-mark" />
        <span className="axiom-brand-name">Axiom</span>
      </div>

      <div className="axiom-section-label">{t.workspace}</div>
      <button className="axiom-nav-btn" onClick={openFolder}>
        {t.open}
      </button>
      <button className="axiom-nav-btn" onClick={openCollectionSetup}>
        {t.createNew}
      </button>

      {hasCollection && (
        <button
          className={`axiom-nav-item ${view === 'overview' ? 'active' : ''}`}
          onClick={goToOverview}
        >
          <span>{t.overview}</span>
        </button>
      )}

      <div className="axiom-section-label">{t.cases}</div>
      {groups.length ? (
        groups.map((group) => {
          const { method, path } = splitEndpointLabel(group.label);
          return (
            <div key={group.label}>
              <div className="axiom-case-group">
                {method ? <MethodBadge method={method} /> : null}
                <span className="axiom-mono" style={{ fontSize: 11 }}>
                  {path}
                </span>
              </div>
              {group.items.map((item) => (
                <button
                  key={item.fileName}
                  className={`axiom-case-row ${activeFile === item.fileName ? 'active' : ''}`}
                  onClick={() => openTest(item.fileName)}
                >
                  <span className="case-name">{item.name}</span>
                </button>
              ))}
            </div>
          );
        })
      ) : (
        <p className="axiom-mono" style={{ padding: '4px 9px', fontSize: 11.5 }}>
          {folder ? t.empty : ''}
        </p>
      )}

      <div className="axiom-sidebar-foot">
        <label className="form-label" htmlFor="themeSelect">
          {t.theme}
        </label>
        <select
          id="themeSelect"
          className="form-select form-select-sm mb-2"
          value={theme}
          onChange={(event) => setTheme(event.target.value)}
        >
          <option value="system">{t.themeSystem}</option>
          <option value="light">{t.themeLight}</option>
          <option value="dark">{t.themeDark}</option>
        </select>
        <label className="form-label" htmlFor="languageSelect">
          {t.language}
        </label>
        <select
          id="languageSelect"
          className="form-select form-select-sm"
          value={language}
          onChange={(event) => setLanguage(event.target.value)}
        >
          <option value="en">English</option>
          <option value="tr">Türkçe</option>
        </select>
      </div>
    </aside>
  );
}
