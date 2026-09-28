import React, { useEffect, useRef, useState } from 'react';
import { api } from '../api.js';
import Icon from './Icons.jsx';

/**
 * Picks a folder on this machine (a browser cannot show the system's folder dialog for local paths, so
 * the axiom program lists them). Resolves with { folderPath, hasCollection } through `select`.
 */
export default function FolderPicker({ t, initialPath, select, cancel }) {
  const [listing, setListing] = useState(null);
  const [pathInput, setPathInput] = useState('');
  const [error, setError] = useState(null);
  const [newName, setNewName] = useState(null);
  const list = useRef(null);

  const go = async (path) => {
    setError(null);
    try {
      const next = await api.listDirectories(path);
      setListing(next);
      setPathInput(next.path);
      list.current?.scrollTo(0, 0);
    } catch (failure) {
      setError(failure.message);
    }
  };

  useEffect(() => {
    // Start where the user was last; if that folder is gone, start at home.
    api
      .listDirectories(initialPath)
      .catch(() => api.listDirectories())
      .then((first) => {
        setListing(first);
        setPathInput(first.path);
      })
      .catch((failure) => setError(failure.message));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const createFolder = async (event) => {
    event.preventDefault();
    try {
      const created = await api.makeDirectory(listing.path, newName);
      setNewName(null);
      await go(created.path);
    } catch (failure) {
      setError(failure.message);
    }
  };

  return (
    <div
      className="dialog-backdrop"
      onMouseDown={(event) => event.target === event.currentTarget && cancel()}
    >
      <div
        className="dialog folder-picker"
        role="dialog"
        aria-modal="true"
        aria-labelledby="folder-picker-title"
        onKeyDown={(event) => event.key === 'Escape' && cancel()}
      >
        <h3 id="folder-picker-title">{t.chooseFolderTitle}</h3>
        <form
          className="picker-path"
          onSubmit={(event) => {
            event.preventDefault();
            go(pathInput);
          }}
        >
          <button
            type="button"
            className="btn-icon"
            title={t.folderUp}
            aria-label={t.folderUp}
            disabled={!listing?.parent}
            onClick={() => go(listing.parent)}
          >
            <Icon name="arrowUp" size={15} />
          </button>
          <input
            className="form-control font-monospace"
            value={pathInput}
            aria-label={t.folderLabel}
            spellCheck={false}
            onChange={(event) => setPathInput(event.target.value)}
          />
        </form>
        <div className="picker-places">
          {listing && (
            <button type="button" className="btn btn-ghost btn-sm" onClick={() => go(listing.home)}>
              {t.folderHome}
            </button>
          )}
          {(listing?.roots || []).map((root) => (
            <button
              key={root.path}
              type="button"
              className="btn btn-ghost btn-sm mono"
              onClick={() => go(root.path)}
            >
              {root.name}
            </button>
          ))}
        </div>
        <div className="picker-list" ref={list}>
          {listing && !listing.directories.length && (
            <p className="muted small picker-empty">{t.noSubfolders}</p>
          )}
          {(listing?.directories || []).map((directory) => (
            <div key={directory.path} className="picker-row">
              <button type="button" className="picker-item" onClick={() => go(directory.path)}>
                <Icon name="folder" size={15} />
                <span className="picker-name">{directory.name}</span>
                {directory.hasCollection && (
                  <span className="chip chip-good">{t.collectionBadge}</span>
                )}
              </button>
              {directory.hasCollection && (
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  onClick={() => select({ folderPath: directory.path, hasCollection: true })}
                >
                  {t.openShort}
                </button>
              )}
            </div>
          ))}
        </div>
        {newName !== null && (
          <form className="picker-new" onSubmit={createFolder}>
            <input
              className="form-control"
              value={newName}
              placeholder={t.newFolderName}
              aria-label={t.newFolderName}
              autoFocus
              onChange={(event) => setNewName(event.target.value)}
            />
            <button type="submit" className="btn btn-secondary btn-sm" disabled={!newName.trim()}>
              {t.createFolder}
            </button>
            <button type="button" className="btn btn-ghost btn-sm" onClick={() => setNewName(null)}>
              {t.cancelRun}
            </button>
          </form>
        )}
        {error && (
          <p className="field-hint warn" role="alert">
            {error}
          </p>
        )}
        <div className="dialog-actions">
          {newName === null && (
            <button type="button" className="btn btn-ghost me-auto" onClick={() => setNewName('')}>
              <Icon name="folderPlus" size={14} /> {t.newFolder}
            </button>
          )}
          {listing?.hasCollection && <span className="muted small">{t.containsCollection}</span>}
          <button type="button" className="btn btn-ghost" onClick={cancel}>
            {t.cancelRun}
          </button>
          <button
            type="button"
            className="btn btn-primary"
            disabled={!listing}
            onClick={() =>
              select({ folderPath: listing.path, hasCollection: listing.hasCollection })
            }
          >
            {t.useThisFolder}
          </button>
        </div>
      </div>
    </div>
  );
}
