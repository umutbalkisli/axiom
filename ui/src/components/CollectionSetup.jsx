import React, { useState } from 'react';
import Icon from './Icons.jsx';

export default function CollectionSetup({ t, mode, createNew, importOpenApi, cancel }) {
  const [name, setName] = useState('');
  const [specificationUrl, setSpecificationUrl] = useState('');
  const [tab, setTab] = useState(mode === 'import' ? 'import' : 'empty');
  const ready = name.trim() && (tab === 'empty' || specificationUrl.trim());

  const submit = () => {
    if (!ready) return;
    if (tab === 'empty') createNew(name.trim());
    else importOpenApi(name.trim(), specificationUrl.trim());
  };

  return (
    <div className="page narrow">
      <button type="button" className="btn btn-ghost btn-sm back-link" onClick={cancel}>
        <Icon name="arrowLeft" size={14} /> {t.back}
      </button>
      <header className="page-header">
        <div>
          <h2>{t.setupTitle}</h2>
          <p>{t.setupSub}</p>
        </div>
      </header>

      <div className="panel">
        <div className="panel-body">
          <label className="field">
            <span className="field-label">{t.collectionName}</span>
            <input
              className="form-control"
              autoFocus
              value={name}
              placeholder={t.collectionNamePlaceholder}
              onChange={(event) => setName(event.target.value)}
              onKeyDown={(event) => event.key === 'Enter' && submit()}
            />
          </label>

          <div className="tabs compact" role="tablist">
            <button
              type="button"
              role="tab"
              aria-selected={tab === 'empty'}
              className={`tab ${tab === 'empty' ? 'active' : ''}`}
              onClick={() => setTab('empty')}
            >
              {t.emptyCollectionTitle}
            </button>
            <button
              type="button"
              role="tab"
              aria-selected={tab === 'import'}
              className={`tab ${tab === 'import' ? 'active' : ''}`}
              onClick={() => setTab('import')}
            >
              {t.importOpenApiTitle}
            </button>
          </div>

          {tab === 'empty' ? (
            <p className="muted">{t.emptyCollectionDescription}</p>
          ) : (
            <label className="field">
              <span className="field-label">{t.importOpenApiDescription}</span>
              <input
                className="form-control font-monospace"
                type="url"
                placeholder={t.openApiUrlPlaceholder}
                value={specificationUrl}
                onChange={(event) => setSpecificationUrl(event.target.value)}
                onKeyDown={(event) => event.key === 'Enter' && submit()}
              />
            </label>
          )}
        </div>
        <div className="panel-footer">
          <span className="muted">{t.chooseFolderHint}</span>
          <button type="button" className="btn btn-primary" disabled={!ready} onClick={submit}>
            {tab === 'empty' ? t.createNew : t.importOpenApi}
          </button>
        </div>
      </div>
    </div>
  );
}
