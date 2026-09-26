import React from 'react';

const METHOD_CLASS = {
  GET: 'method-get',
  POST: 'method-post',
  PUT: 'method-put',
  PATCH: 'method-put',
  DELETE: 'method-delete',
};

export function MethodBadge({ method }) {
  const key = String(method || 'GET').toUpperCase();
  return <span className={`method-badge ${METHOD_CLASS[key] || 'method-get'}`}>{key}</span>;
}

export function StatusBadge({ tone, children }) {
  return (
    <span className={`status-badge status-${tone}`}>
      <span className="status-dot" />
      {children}
    </span>
  );
}

// Splits the "METHOD /path" labels produced by groupByEndpoint() in i18n.js
// so the method can be rendered as a colored badge instead of plain text.
export function splitEndpointLabel(label) {
  const spaceIndex = String(label || '').indexOf(' ');
  if (spaceIndex === -1) return { method: null, path: label };
  return { method: label.slice(0, spaceIndex), path: label.slice(spaceIndex + 1) };
}
