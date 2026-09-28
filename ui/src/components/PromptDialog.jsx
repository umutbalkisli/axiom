import React, { useEffect, useRef, useState } from 'react';

/**
 * A small modal that asks for one value (Electron has no window.prompt). `submit` may throw: its message is
 * shown in the dialog, which stays open so the value can be corrected.
 */
export default function PromptDialog({
  t,
  title,
  label,
  hint,
  initialValue = '',
  placeholder,
  suggestions = [],
  confirmLabel,
  submit,
  close,
}) {
  const [value, setValue] = useState(initialValue);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const input = useRef(null);

  useEffect(() => {
    input.current?.focus();
    input.current?.select();
  }, []);

  const confirm = async (event) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await submit(value.trim());
      close();
    } catch (failure) {
      setError(failure.message);
      setBusy(false);
    }
  };

  return (
    <div
      className="dialog-backdrop"
      onMouseDown={(event) => event.target === event.currentTarget && close()}
    >
      <form
        className="dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="prompt-dialog-title"
        onSubmit={confirm}
        onKeyDown={(event) => event.key === 'Escape' && close()}
      >
        <h3 id="prompt-dialog-title">{title}</h3>
        <label className="field">
          <span className="field-label">{label}</span>
          <input
            ref={input}
            className="form-control font-monospace"
            value={value}
            placeholder={placeholder}
            list="prompt-dialog-suggestions"
            spellCheck={false}
            onChange={(event) => setValue(event.target.value)}
          />
          {hint && <span className="field-hint">{hint}</span>}
        </label>
        <datalist id="prompt-dialog-suggestions">
          {suggestions.map((item) => (
            <option key={item} value={item} />
          ))}
        </datalist>
        {error && (
          <p className="field-hint warn" role="alert">
            {error}
          </p>
        )}
        <div className="dialog-actions">
          <button type="button" className="btn btn-ghost" onClick={close}>
            {t.cancelRun}
          </button>
          <button type="submit" className="btn btn-primary" disabled={busy}>
            {confirmLabel}
          </button>
        </div>
      </form>
    </div>
  );
}
