import React, { useEffect, useRef, useState } from 'react';
import { methodSupportsBody, toVariableName, yamlPreview } from '../i18n.js';
import { MethodBadge } from './Badge.jsx';
import Icon from './Icons.jsx';
import { VariableInput, VariableTextarea } from './VariableField.jsx';

const METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'];
const OPERATORS = [
  ['==', '=='],
  ['!=', '!='],
  ['>', '>'],
  ['>=', '>='],
  ['<', '<'],
  ['<=', '<='],
  ['contains', 'contains'],
  ['not_contains', 'not_contains'],
  ['exists', 'exists'],
  ['not_exists', 'not_exists'],
];
const NO_EXPECTED = new Set(['exists', 'not_exists']);

function Field({ label, hint, className = '', children }) {
  return (
    <label className={`field ${className}`}>
      <span className="field-label">{label}</span>
      {children}
      {hint && <span className="field-hint">{hint}</span>}
    </label>
  );
}

function KeyValueEditor({ t, rows, onChange, keyPlaceholder, valuePlaceholder, addLabel }) {
  const updateRow = (index, field, value) =>
    onChange(rows.map((row, rowIndex) => (rowIndex === index ? { ...row, [field]: value } : row)));
  return (
    <div className="kv-editor">
      {rows.map((row, index) => (
        <div className="kv-row" key={index}>
          <input
            className="form-control font-monospace"
            value={row.key}
            placeholder={keyPlaceholder}
            aria-label={keyPlaceholder}
            onChange={(event) => updateRow(index, 'key', event.target.value)}
          />
          <VariableInput
            value={row.value}
            placeholder={valuePlaceholder}
            onChange={(event) => updateRow(index, 'value', event.target.value)}
          />
          <button
            type="button"
            className="btn-icon danger"
            aria-label={t.remove}
            title={t.remove}
            onClick={() => onChange(rows.filter((_, rowIndex) => rowIndex !== index))}
          >
            <Icon name="x" size={14} />
          </button>
        </div>
      ))}
      <button
        type="button"
        className="btn btn-link"
        onClick={() => onChange([...rows, { key: '', value: '' }])}
      >
        <Icon name="plus" size={13} /> {addLabel}
      </button>
    </div>
  );
}

function AssertionsEditor({
  t,
  step,
  index,
  aggregations,
  savedNames,
  variableNames,
  addAssertion,
  updateAssertion,
  removeAssertion,
}) {
  // Everything an assertion can read: this step's own result, results saved by steps before it
  // (and by this step itself), and the collection / test variables.
  const sources = [
    ...(step.type === 'request'
      ? ['status', 'duration_ms', 'body']
      : ['row_count', 'duration_ms', 'rows']
    ).map((name) => [name, t.sourceResult]),
    ...savedNames.map((name) => [name, t.sourceSaved]),
    ...variableNames.map((name) => [name, t.sourceVariable]),
  ].filter(([name], position, all) => all.findIndex(([other]) => other === name) === position);
  const listId = `assertion-${index}-sources`;
  return (
    <section className="assertions">
      <div className="section-head">
        <h4>
          {t.assertions} <span className="count-pill">{step.assertions.length}</span>
        </h4>
      </div>
      <datalist id={listId}>
        {sources.map(([name, label]) => (
          <option key={name} value={name} label={label} />
        ))}
      </datalist>
      {step.assertions.length === 0 ? (
        <p className="muted small">{t.noAssertions}</p>
      ) : (
        <div className="assertion-grid">
          <div className="assertion-head">
            <span>{t.assertionCheck}</span>
            <span>{t.aggregation}</span>
            <span>{t.operator}</span>
            <span>{t.expected}</span>
            <span />
          </div>
          {step.assertions.map((assertion, assertionIndex) => (
            <div className="assertion-row" key={assertionIndex}>
              <input
                className="form-control font-monospace"
                list={listId}
                value={assertion.expression}
                placeholder={t.assertionValue}
                aria-label={t.assertionCheck}
                spellCheck={false}
                onChange={(event) =>
                  updateAssertion(index, assertionIndex, { expression: event.target.value })
                }
              />
              <select
                className="form-select"
                aria-label={t.aggregation}
                title={t.aggregationHint}
                value={assertion.aggregate || ''}
                onChange={(event) =>
                  updateAssertion(index, assertionIndex, { aggregate: event.target.value })
                }
              >
                <option value="">{t.aggregationNone}</option>
                {aggregations.map((name) => (
                  <option key={name} value={name}>
                    {name}
                  </option>
                ))}
              </select>
              <select
                className="form-select"
                aria-label={t.operator}
                value={assertion.operator}
                onChange={(event) =>
                  updateAssertion(index, assertionIndex, { operator: event.target.value })
                }
              >
                {OPERATORS.map(([value, label]) => (
                  <option key={value} value={value}>
                    {t.operatorLabels?.[value] || label}
                  </option>
                ))}
              </select>
              <VariableInput
                value={NO_EXPECTED.has(assertion.operator) ? '' : assertion.expected}
                disabled={NO_EXPECTED.has(assertion.operator)}
                placeholder={NO_EXPECTED.has(assertion.operator) ? '—' : t.expected}
                onChange={(event) =>
                  updateAssertion(index, assertionIndex, { expected: event.target.value })
                }
              />
              <button
                type="button"
                className="btn-icon danger"
                aria-label={t.remove}
                title={t.remove}
                onClick={() => removeAssertion(index, assertionIndex)}
              >
                <Icon name="x" size={14} />
              </button>
            </div>
          ))}
        </div>
      )}
      <button type="button" className="btn btn-link" onClick={() => addAssertion(index)}>
        <Icon name="plus" size={13} /> {t.addAssertion}
      </button>
    </section>
  );
}

function StepCard({
  t,
  step,
  index,
  total,
  open,
  toggle,
  updateStep,
  removeStep,
  moveStep,
  connectionNames,
  ...assertionProps
}) {
  const [panel, setPanel] = useState('params');
  const patch = (key, value) => updateStep(index, { [key]: value });
  const isRequest = step.type === 'request';
  const supportsBody = isRequest && methodSupportsBody(step.method);
  const activePanel = panel === 'body' && !supportsBody ? 'params' : panel;
  const summary = isRequest ? step.url : (step.sql || '').split('\n')[0];

  return (
    <article className={`step-card ${open ? 'open' : ''}`}>
      <header className="step-head">
        <button type="button" className="step-toggle" aria-expanded={open} onClick={toggle}>
          <span className="step-number">{index + 1}</span>
          {isRequest ? (
            <MethodBadge method={step.method} />
          ) : (
            <span className="method-badge method-sql">SQL</span>
          )}
          <span className="step-title">{step.name || t.untitledStep}</span>
          <span className="step-summary mono" title={summary}>
            {summary}
          </span>
          {step.assertions.length > 0 && (
            <span className="chip" title={t.assertions}>
              <Icon name="checkCircle" size={13} /> {step.assertions.length}
            </span>
          )}
          <Icon name={open ? 'chevronDown' : 'chevronRight'} size={15} className="step-chevron" />
        </button>
        <div className="step-tools">
          <button
            type="button"
            className="btn-icon"
            title={t.moveUp}
            aria-label={t.moveUp}
            disabled={index === 0}
            onClick={() => moveStep(index, -1)}
          >
            <Icon name="arrowUp" size={14} />
          </button>
          <button
            type="button"
            className="btn-icon"
            title={t.moveDown}
            aria-label={t.moveDown}
            disabled={index === total - 1}
            onClick={() => moveStep(index, 1)}
          >
            <Icon name="arrowDown" size={14} />
          </button>
          <button
            type="button"
            className="btn-icon danger"
            title={t.remove}
            aria-label={t.remove}
            onClick={() => removeStep(index)}
          >
            <Icon name="trash" size={14} />
          </button>
        </div>
      </header>

      {open && (
        <div className="step-body">
          <div className="form-grid two">
            <Field label={t.name}>
              <input
                className="form-control"
                value={step.name}
                onChange={(event) => patch('name', event.target.value)}
              />
            </Field>
            <Field label={t.saveAs} hint={step.save_as ? `{{${step.save_as}}}` : t.saveAsHint}>
              <input
                className="form-control font-monospace"
                value={step.save_as}
                placeholder={t.resultName}
                onChange={(event) => patch('save_as', toVariableName(event.target.value))}
              />
            </Field>
          </div>

          {isRequest ? (
            <>
              <div className="request-line">
                <select
                  className="form-select method-select"
                  aria-label={t.method}
                  value={step.method}
                  onChange={(event) => patch('method', event.target.value)}
                >
                  {METHODS.map((method) => (
                    <option key={method}>{method}</option>
                  ))}
                </select>
                <VariableInput
                  value={step.url}
                  placeholder="{{base_url}}/todos"
                  aria-label={t.url}
                  onChange={(event) => patch('url', event.target.value)}
                />
              </div>
              <div className="tabs compact" role="tablist">
                {[
                  ['params', t.queryParamsShort, step.queryParams.length],
                  ['headers', t.headersShort, step.headers.length],
                  ...(supportsBody ? [['body', t.bodyShort, step.body ? '•' : 0]] : []),
                ].map(([id, label, count]) => (
                  <button
                    type="button"
                    role="tab"
                    key={id}
                    aria-selected={activePanel === id}
                    className={`tab ${activePanel === id ? 'active' : ''}`}
                    onClick={() => setPanel(id)}
                  >
                    {label}
                    {count ? <span className="count-pill">{count}</span> : null}
                  </button>
                ))}
              </div>
              {activePanel === 'params' && (
                <KeyValueEditor
                  t={t}
                  rows={step.queryParams}
                  onChange={(rows) => patch('queryParams', rows)}
                  keyPlaceholder="page"
                  valuePlaceholder="1"
                  addLabel={t.addQueryParameter}
                />
              )}
              {activePanel === 'headers' && (
                <KeyValueEditor
                  t={t}
                  rows={step.headers}
                  onChange={(rows) => patch('headers', rows)}
                  keyPlaceholder="Authorization"
                  valuePlaceholder="Bearer {{secret.api_token}}"
                  addLabel={t.addHeader}
                />
              )}
              {activePanel === 'body' && (
                <VariableTextarea
                  rows={7}
                  value={step.body}
                  placeholder={'{\n  "title": "New todo"\n}'}
                  onChange={(event) => patch('body', event.target.value)}
                />
              )}
            </>
          ) : (
            <>
              <Field label={t.connection}>
                {connectionNames.length ? (
                  <select
                    className="form-select"
                    value={step.connection}
                    onChange={(event) => patch('connection', event.target.value)}
                  >
                    <option value="">{t.chooseConnection}</option>
                    {[...new Set([...connectionNames, step.connection].filter(Boolean))].map(
                      (name) => (
                        <option key={name} value={name}>
                          {name}
                        </option>
                      ),
                    )}
                  </select>
                ) : (
                  <input
                    className="form-control font-monospace"
                    value={step.connection}
                    placeholder={t.noConnectionsYet}
                    onChange={(event) => patch('connection', event.target.value)}
                  />
                )}
              </Field>
              <Field label={t.sql}>
                <VariableTextarea
                  rows={4}
                  value={step.sql}
                  placeholder="SELECT Id FROM todos LIMIT 1"
                  onChange={(event) => patch('sql', event.target.value)}
                />
              </Field>
            </>
          )}

          <AssertionsEditor t={t} step={step} index={index} {...assertionProps} />
        </div>
      )}
    </article>
  );
}

export default function Builder({
  t,
  test,
  setTest,
  dirty,
  isNew,
  fileName,
  saveTest,
  deleteTest,
  back,
  addStep,
  removeStep,
  moveStep,
  updateStep,
  addAssertion,
  updateAssertion,
  removeAssertion,
  aggregations,
  connectionNames,
  variableNames,
}) {
  const [open, setOpen] = useState(
    () => new Set(test.steps.length <= 3 ? test.steps.map((_, i) => i) : [0]),
  );
  const [showYaml, setShowYaml] = useState(false);
  const previousCount = useRef(test.steps.length);

  // A newly added step opens itself; a removed one shifts the open indexes.
  useEffect(() => {
    if (test.steps.length > previousCount.current) {
      setOpen((current) => new Set([...current, test.steps.length - 1]));
    }
    previousCount.current = test.steps.length;
  }, [test.steps.length]);

  const remove = (index) => {
    setOpen(
      (current) =>
        new Set([...current].filter((i) => i !== index).map((i) => (i > index ? i - 1 : i))),
    );
    removeStep(index);
  };
  const move = (index, direction) => {
    setOpen((current) => {
      const next = new Set(current);
      const wasOpen = current.has(index);
      const otherWasOpen = current.has(index + direction);
      next.delete(index);
      next.delete(index + direction);
      if (wasOpen) next.add(index + direction);
      if (otherWasOpen) next.add(index);
      return next;
    });
    moveStep(index, direction);
  };
  const toggle = (index) =>
    setOpen((current) => {
      const next = new Set(current);
      if (next.has(index)) next.delete(index);
      else next.add(index);
      return next;
    });
  const allOpen = test.steps.length > 0 && open.size >= test.steps.length;
  const canSave = test.name.trim() && test.endpoint.trim();

  return (
    <div className="page">
      <div className="builder-bar">
        <button type="button" className="btn btn-ghost btn-sm back-link" onClick={back}>
          <Icon name="arrowLeft" size={14} /> {t.cases}
        </button>
        <span className="spacer" />
        {dirty && (
          <span className="dirty-label">
            <span className="dirty-dot" /> {t.unsavedChanges}
          </span>
        )}
        <button
          type="button"
          className={`btn btn-ghost btn-sm ${showYaml ? 'active' : ''}`}
          onClick={() => setShowYaml(!showYaml)}
        >
          <Icon name="code" size={14} /> YAML
        </button>
        {!isNew && (
          <button type="button" className="btn btn-ghost btn-sm danger" onClick={deleteTest}>
            <Icon name="trash" size={14} /> {t.delete}
          </button>
        )}
        <button
          type="button"
          className="btn btn-primary"
          disabled={!canSave || (!dirty && !isNew)}
          onClick={saveTest}
        >
          {t.saveTest}
        </button>
      </div>

      <header className="test-header">
        <input
          className="title-input"
          value={test.name}
          placeholder={t.testNamePlaceholder}
          aria-label={t.testName}
          autoFocus={isNew}
          onChange={(event) => setTest({ ...test, name: event.target.value })}
        />
        <input
          className="subtitle-input"
          value={test.description}
          placeholder={t.descriptionPlaceholder}
          aria-label={t.description}
          onChange={(event) => setTest({ ...test, description: event.target.value })}
        />
        <div className="endpoint-line">
          <select
            className="form-select method-select"
            aria-label={t.method}
            value={test.method}
            onChange={(event) => setTest({ ...test, method: event.target.value })}
          >
            {METHODS.map((method) => (
              <option key={method}>{method}</option>
            ))}
          </select>
          <input
            className="form-control font-monospace"
            value={test.endpoint}
            placeholder="/todos/{id}"
            aria-label={t.endpoint}
            onChange={(event) => setTest({ ...test, endpoint: event.target.value })}
          />
        </div>
        <p className="field-hint">{t.endpointHint}</p>
        <p className="field-hint file-hint" title={t.fileHint}>
          <Icon name="code" size={12} />
          <span className="mono">{fileName ? `tests/${fileName}` : t.newFileHint}</span>
        </p>
        {!canSave && dirty && <p className="field-hint warn">{t.nameAndEndpointRequired}</p>}
      </header>

      <div className="section-head steps-head">
        <h3>
          {t.steps} <span className="count-pill">{test.steps.length}</span>
        </h3>
        {test.steps.length > 1 && (
          <button
            type="button"
            className="btn btn-link"
            onClick={() =>
              setOpen(allOpen ? new Set() : new Set(test.steps.map((_, index) => index)))
            }
          >
            {allOpen ? t.collapseAll : t.expandAll}
          </button>
        )}
      </div>

      <div className="steps">
        {test.steps.map((step, index) => (
          <StepCard
            key={index}
            t={t}
            step={step}
            index={index}
            total={test.steps.length}
            open={open.has(index)}
            toggle={() => toggle(index)}
            updateStep={updateStep}
            removeStep={remove}
            moveStep={move}
            connectionNames={connectionNames}
            aggregations={aggregations}
            addAssertion={addAssertion}
            updateAssertion={updateAssertion}
            removeAssertion={removeAssertion}
            savedNames={test.steps
              .slice(0, index + 1)
              .map((previous) => previous.save_as)
              .filter(Boolean)}
            variableNames={variableNames}
          />
        ))}
        <div className="add-step">
          {test.steps.length === 0 && <p className="muted">{t.noSteps}</p>}
          <div className="add-step-buttons">
            <button type="button" className="btn btn-dashed" onClick={() => addStep('request')}>
              <Icon name="globe" size={15} /> {t.requestStep}
            </button>
            <button type="button" className="btn btn-dashed" onClick={() => addStep('db_query')}>
              <Icon name="database" size={15} /> {t.dbStep}
            </button>
          </div>
        </div>
      </div>

      {showYaml && (
        <section className="yaml-panel">
          <div className="section-head">
            <h4>{t.yaml}</h4>
          </div>
          <pre>{yamlPreview(test, t)}</pre>
        </section>
      )}
    </div>
  );
}
