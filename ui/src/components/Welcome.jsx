import React from 'react';
import Icon from './Icons.jsx';

function ActionCard({ icon, title, description, onClick }) {
  return (
    <button type="button" className="action-card" onClick={onClick}>
      <span className="action-icon">
        <Icon name={icon} size={20} />
      </span>
      <span className="action-title">{title}</span>
      <span className="action-desc">{description}</span>
    </button>
  );
}

export default function Welcome({
  t,
  folder,
  recent,
  openFolder,
  openRecent,
  openCollectionSetup,
  openImport,
}) {
  return (
    <div className="page welcome">
      <div className="welcome-hero">
        <span className="brand-mark large" />
        <h2>{t.welcomeTitle}</h2>
        <p>{t.welcomeSub}</p>
      </div>

      {folder && (
        <div className="notice info">
          <div>
            <strong>{t.folderWithoutCollection}</strong>
            <div className="mono muted">{folder}</div>
          </div>
          <button type="button" className="btn btn-primary btn-sm" onClick={openCollectionSetup}>
            {t.createHere}
          </button>
        </div>
      )}

      <div className="action-grid">
        <ActionCard
          icon="folder"
          title={t.open}
          description={t.openDescription}
          onClick={openFolder}
        />
        <ActionCard
          icon="folderPlus"
          title={t.createNew}
          description={t.emptyCollectionDescription}
          onClick={openCollectionSetup}
        />
        <ActionCard
          icon="download"
          title={t.importOpenApi}
          description={t.importOpenApiShort}
          onClick={openImport}
        />
      </div>

      {recent.length > 0 && (
        <section className="recent">
          <h3 className="section-title">{t.recent}</h3>
          <div className="list-card">
            {recent.map((path) => (
              <button
                type="button"
                key={path}
                className="list-row"
                onClick={() => openRecent(path)}
              >
                <Icon name="folder" size={16} />
                <span className="list-row-main">
                  <span className="list-row-title">{path.split(/[\\/]/).pop()}</span>
                  <span className="mono muted">{path}</span>
                </span>
                <Icon name="chevronRight" size={14} />
              </button>
            ))}
          </div>
        </section>
      )}
    </div>
  );
}
