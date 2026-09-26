import React, { useState } from 'react';
import { methodSupportsBody, toVariableName, yamlPreview } from '../i18n.js';
import { MethodBadge } from './Badge.jsx';
import { VariableInput, VariableTextarea } from './VariableField.jsx';

function Field({ label, className = '', children }) {
  return (
    <label className={`${className} d-block`}>
      <span className="form-label mb-1">{label}</span>
      {children}
    </label>
  );
}

function KeyValueEditor({ t, label, rows, onChange, keyPlaceholder, valuePlaceholder }) {
  const updateRow = (index, field, value) => {
    onChange(rows.map((row, rowIndex) => (rowIndex === index ? { ...row, [field]: value } : row)));
  };

  const addRow = () => onChange([...rows, { key: '', value: '' }]);
  const removeRow = (index) => onChange(rows.filter((_, rowIndex) => rowIndex !== index));

  return (
    <div className="mb-3">
      <div className="d-flex justify-content-between align-items-center mb-2">
        <span className="form-label mb-0">{label}</span>
        <button type="button" className="btn btn-sm btn-outline-secondary" onClick={addRow}>
          {t.addQueryParameter}
        </button>
      </div>
      {rows.map((row, index) => (
        <div className="d-flex gap-2 mb-2" key={`${label}-${index}`}>
          <input
            className="form-control font-monospace"
            style={{ flex: '0 1 38%' }}
            value={row.key}
            placeholder={keyPlaceholder}
            onChange={(event) => updateRow(index, 'key', event.target.value)}
          />
          <VariableInput
            className="flex-grow-1"
            value={row.value}
            placeholder={valuePlaceholder}
            onChange={(event) => updateRow(index, 'value', event.target.value)}
          />
          <button
            type="button"
            className="btn btn-outline-danger px-2"
            aria-label={t.remove}
            title={t.remove}
            onClick={() => removeRow(index)}
          >
            &times;
          </button>
        </div>
      ))}
    </div>
  );
}
function StepCard({
  t,
  step,
  index,
  updateStep,
  removeStep,
  addAssertion,
  updateAssertion,
  removeAssertion,
  aggregations,
  savedNames,
}) {
  const patch = (key, value) => updateStep(index, { [key]: value });
  const listId = `assertion-${index}`;
  const sources = [
    ...(step.type === 'request'
      ? ['status', 'duration_ms', 'body']
      : ['row_count', 'duration_ms', 'rows']),
    ...savedNames,
  ];
  return (
    <article className="card mb-3">
      <div className="card-header d-flex align-items-center justify-content-between">
        <span className="d-flex align-items-center gap-2">
          <span className="axiom-mono" style={{ color: 'var(--fg-subtle)' }}>
            {t.step} {index + 1}
          </span>
          {step.type === 'request' ? (
            <MethodBadge method={step.method} />
          ) : (
            <span
              className="method-badge"
              style={{ color: 'var(--fg-muted)', background: 'var(--canvas-inset)' }}
            >
              SQL
            </span>
          )}
          <span>{step.name}</span>
        </span>
        <button className="btn btn-sm btn-outline-danger" onClick={() => removeStep(index)}>
          {t.remove}
        </button>
      </div>
      <div className="card-body">
        <div className="row g-2">
          <Field label={t.saveAs} className="col-md-4">
            <input
              className="form-control font-monospace"
              value={step.save_as}
              placeholder={t.resultName}
              onChange={(e) => patch('save_as', toVariableName(e.target.value))}
            />
          </Field>
          <Field label={t.name} className="col-md-3">
            <input
              className="form-control"
              value={step.name}
              onChange={(e) => patch('name', e.target.value)}
            />
          </Field>
          {step.type === 'request' ? (
            <Field label={t.method} className="col-md-3">
              <select
                className="form-select"
                value={step.method}
                onChange={(e) => patch('method', e.target.value)}
              >
                <option>GET</option>
                <option>POST</option>
                <option>PUT</option>
                <option>PATCH</option>
                <option>DELETE</option>
              </select>
            </Field>
          ) : (
            <Field label={t.connection} className="col-md-3">
              <input
                className="form-control font-monospace"
                value={step.connection}
                onChange={(e) => patch('connection', e.target.value)}
              />
            </Field>
          )}
        </div>
        <Field label={step.type === 'request' ? t.url : t.sql} className="mt-3">
          {step.type === 'request' ? (
            <VariableInput value={step.url} onChange={(e) => patch('url', e.target.value)} />
          ) : (
            <VariableTextarea
              rows={3}
              value={step.sql}
              onChange={(e) => patch('sql', e.target.value)}
            />
          )}
        </Field>
        {step.type === 'request' && (
          <>
            <div className="row g-3 mt-1">
              <div className="col-md-6">
                <KeyValueEditor
                  t={t}
                  label={t.queryParams}
                  rows={step.queryParams}
                  onChange={(rows) => patch('queryParams', rows)}
                  keyPlaceholder="page"
                  valuePlaceholder="1"
                />
              </div>
              <div className="col-md-6">
                <KeyValueEditor
                  t={t}
                  label={t.headers}
                  rows={step.headers}
                  onChange={(rows) => patch('headers', rows)}
                  keyPlaceholder="Authorization"
                  valuePlaceholder="Bearer {{token}}"
                />
              </div>
            </div>
            {methodSupportsBody(step.method) && (
              <Field label={t.body} className="mt-1">
                <VariableTextarea
                  rows={6}
                  value={step.body}
                  placeholder={'{\n  "title": "New todo"\n}'}
                  onChange={(e) => patch('body', e.target.value)}
                />
              </Field>
            )}
          </>
        )}
        <div className="mt-4 pt-3" style={{ borderTop: '1px dashed var(--border-muted)' }}>
          <div className="form-label mb-2">{t.addAssertion.replace('+ ', '')}</div>
          <datalist id={`${listId}-sources`}>
            {sources.map((name) => (
              <option key={name} value={name} />
            ))}
          </datalist>
          {step.assertions.map((assertion, assertionIndex) => (
            <div
              className="row g-2 mb-2 align-items-center"
              key={`${step.id}-assertion-${assertionIndex}`}
            >
              <div className="col-md-5">
                <input
                  className="form-control font-monospace"
                  list={`${listId}-sources`}
                  value={assertion.expression}
                  placeholder={t.assertionValue}
                  spellCheck={false}
                  onChange={(e) =>
                    updateAssertion(index, assertionIndex, { expression: e.target.value })
                  }
                />
              </div>
              <div className="col-md-2">
                <select
                  className="form-select"
                  aria-label={t.aggregation}
                  title={t.aggregationHint}
                  value={assertion.aggregate || ''}
                  onChange={(e) =>
                    updateAssertion(index, assertionIndex, { aggregate: e.target.value })
                  }
                >
                  <option value="">{t.aggregationNone}</option>
                  {aggregations.map((name) => (
                    <option key={name} value={name}>
                      {name}
                    </option>
                  ))}
                </select>
              </div>
              <div className="col-md-2">
                <select
                  className="form-select"
                  value={assertion.operator}
                  onChange={(e) =>
                    updateAssertion(index, assertionIndex, { operator: e.target.value })
                  }
                >
                  <option>==</option>
                  <option>!=</option>
                  <option>&gt;</option>
                  <option>&gt;=</option>
                  <option>&lt;</option>
                  <option>&lt;=</option>
                  <option>contains</option>
                  <option>exists</option>
                </select>
              </div>
              <div className="col-md-2">
                <VariableInput
                  value={assertion.expected}
                  placeholder="expected"
                  onChange={(e) =>
                    updateAssertion(index, assertionIndex, { expected: e.target.value })
                  }
                />
              </div>
              <div className="col-auto">
                <button
                  className="btn btn-outline-danger px-2"
                  aria-label={t.remove}
                  title={t.remove}
                  onClick={() => removeAssertion(index, assertionIndex)}
                >
                  &times;
                </button>
              </div>
            </div>
          ))}
          <div className="d-flex justify-content-end">
            <button
              className="btn btn-sm btn-outline-secondary"
              onClick={() => addAssertion(index)}
            >
              {t.addAssertion}
            </button>
          </div>
        </div>
      </div>
    </article>
  );
}

export default function Builder({
  t,
  test,
  setTest,
  saveTest,
  deleteTest,
  addStep,
  removeStep,
  updateStep,
  addAssertion,
  updateAssertion,
  removeAssertion,
  aggregations,
}) {
  return (
    <section>
      <div className="card mb-3">
        <div className="card-header d-flex justify-content-between align-items-center gap-2">
          <span>{test.name ? `${t.builder} — ${test.name}` : t.builder}</span>
          <div className="d-flex flex-wrap gap-2">
            <button className="btn btn-sm btn-outline-primary" onClick={() => addStep('request')}>
              {t.requestStep}
            </button>
            <button className="btn btn-sm btn-outline-primary" onClick={() => addStep('db_query')}>
              {t.dbStep}
            </button>
            <button className="btn btn-sm btn-primary" onClick={saveTest}>
              {t.saveTest}
            </button>
            <button
              className="btn btn-sm btn-outline-danger"
              disabled={!test.name}
              onClick={deleteTest}
            >
              {t.delete}
            </button>
          </div>
        </div>
        <div className="card-body">
          <div className="row g-3 mb-3">
            <Field label={t.method} className="col-md-2">
              <select
                className="form-select"
                value={test.method}
                onChange={(e) => setTest({ ...test, method: e.target.value })}
              >
                <option>GET</option>
                <option>POST</option>
                <option>PUT</option>
                <option>PATCH</option>
                <option>DELETE</option>
              </select>
            </Field>
            <Field label={t.endpoint} className="col-md-10">
              <input
                className="form-control font-monospace"
                value={test.endpoint}
                onChange={(e) => setTest({ ...test, endpoint: e.target.value })}
              />
            </Field>
          </div>
          <div className="row g-3 mb-3">
            <Field label={t.testName} className="col-md-4">
              <input
                className="form-control"
                value={test.name}
                onChange={(e) => setTest({ ...test, name: e.target.value })}
              />
            </Field>
            <Field label={t.description} className="col-md-8">
              <input
                className="form-control"
                value={test.description}
                onChange={(e) => setTest({ ...test, description: e.target.value })}
              />
            </Field>
          </div>
          {test.steps.length ? (
            test.steps.map((step, index) => (
              <StepCard
                key={`${step.id}-${index}`}
                t={t}
                step={step}
                index={index}
                updateStep={updateStep}
                removeStep={removeStep}
                addAssertion={addAssertion}
                updateAssertion={updateAssertion}
                removeAssertion={removeAssertion}
                aggregations={aggregations}
                savedNames={test.steps
                  .slice(0, index)
                  .map((previous) => previous.save_as)
                  .filter(Boolean)}
              />
            ))
          ) : (
            <p className="text-secondary mb-0">{t.noSteps}</p>
          )}
        </div>
      </div>
      <YamlPanel t={t} test={test} />
    </section>
  );
}

function YamlPanel({ t, test }) {
  const [open, setOpen] = useState(false);
  return (
    <div className="card">
      <div className="card-header d-flex justify-content-between align-items-center">
        <span>{t.yaml}</span>
        <button className="btn btn-sm btn-outline-secondary" onClick={() => setOpen(!open)}>
          {open ? t.hideYaml : t.showYaml}
        </button>
      </div>
      {open && <pre className="card-body mb-0 text-light">{yamlPreview(test, t)}</pre>}
    </div>
  );
}
