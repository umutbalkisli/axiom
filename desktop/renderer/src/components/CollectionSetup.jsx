import React, { useState } from 'react';

export default function CollectionSetup({ t, createNew, importOpenApi }) {
  const [name, setName] = useState('');
  const [specificationUrl, setSpecificationUrl] = useState('');

  return (
    <section style={{ maxWidth: 860 }}>
      <div className="mb-4">
        <h2 className="h5 mb-1">{t.setupTitle}</h2>
        <p className="text-secondary mb-0">{t.chooseFolder}</p>
      </div>
      <div className="card mb-3">
        <div className="card-body">
          <label className="form-label" htmlFor="collectionName">
            {t.collectionName}
          </label>
          <input
            id="collectionName"
            className="form-control"
            value={name}
            placeholder={t.collectionNamePlaceholder}
            onChange={(event) => setName(event.target.value)}
          />
        </div>
      </div>
      <div className="row g-3">
        <div className="col-md-6">
          <div className="card h-100">
            <div className="card-body d-flex flex-column">
              <h3 className="h6 mb-2">{t.emptyCollectionTitle}</h3>
              <p className="text-secondary flex-grow-1">{t.emptyCollectionDescription}</p>
              <button
                className="btn btn-primary"
                disabled={!name.trim()}
                onClick={() => createNew(name.trim())}
              >
                {t.createNew}
              </button>
            </div>
          </div>
        </div>
        <div className="col-md-6">
          <div className="card h-100">
            <div className="card-body d-flex flex-column">
              <h3 className="h6 mb-2">{t.importOpenApiTitle}</h3>
              <p className="text-secondary flex-grow-1">{t.importOpenApiDescription}</p>
              <input
                className="form-control font-monospace mb-3"
                type="url"
                placeholder={t.openApiUrlPlaceholder}
                value={specificationUrl}
                onChange={(event) => setSpecificationUrl(event.target.value)}
              />
              <button
                className="btn btn-outline-primary"
                disabled={!name.trim() || !specificationUrl.trim()}
                onClick={() => importOpenApi(name.trim(), specificationUrl.trim())}
              >
                {t.importOpenApi}
              </button>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
