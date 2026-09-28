import React, { useRef } from 'react';

const VAR_REGEX = /\{\{[^}]+\}\}/g;

// Splits text into plain strings and {{token}} matches so the overlay can
// render each token as a colored <mark>. No dangerouslySetInnerHTML — plain
// React children, so nothing needs manual HTML-escaping.
function renderHighlighted(text) {
  const parts = [];
  let lastIndex = 0;
  let match;
  let key = 0;
  VAR_REGEX.lastIndex = 0;
  while ((match = VAR_REGEX.exec(text))) {
    if (match.index > lastIndex) parts.push(text.slice(lastIndex, match.index));
    parts.push(
      <mark className="var-token" key={key++}>
        {match[0]}
      </mark>,
    );
    lastIndex = match.index + match[0].length;
  }
  parts.push(text.slice(lastIndex));
  if (!text) parts.push('\u00a0'); // keep the overlay's height when empty
  if (text.endsWith('\n')) parts.push(<br key="trailing" />);
  return parts;
}

// Keeps the highlight layer's scroll position in sync with the real field
// (only relevant for multi-line textareas; single-line inputs stay put).
function syncScroll(event) {
  const el = event.target;
  const overlay = el.previousElementSibling;
  if (overlay) {
    overlay.scrollTop = el.scrollTop;
    overlay.scrollLeft = el.scrollLeft;
  }
}

export function VariableInput({ className = '', value, ...rest }) {
  return (
    <div className={`var-field ${className}`}>
      <div className="var-field-highlight var-field-highlight-single" aria-hidden="true">
        {renderHighlighted(value || '')}
      </div>
      <input
        className="var-field-input form-control"
        value={value}
        onScroll={syncScroll}
        spellCheck={false}
        {...rest}
      />
    </div>
  );
}

export function VariableTextarea({ className = '', value, rows = 3, ...rest }) {
  const ref = useRef(null);
  return (
    <div className={`var-field ${className}`}>
      <div className="var-field-highlight" aria-hidden="true">
        {renderHighlighted(value || '')}
      </div>
      <textarea
        ref={ref}
        className="var-field-input form-control"
        rows={rows}
        value={value}
        onScroll={syncScroll}
        spellCheck={false}
        {...rest}
      />
    </div>
  );
}
