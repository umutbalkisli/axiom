import React, { useEffect, useRef, useState } from 'react';
import { methodSupportsBody, toVariableName, yamlPreview } from '../i18n.js';
import { MethodBadge } from './Badge.jsx';
import Icon from './Icons.jsx';
import ResponsePreview from './ResponsePreview.jsx';
import { VariableInput, VariableTextarea } from './VariableField.jsx';

const METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'];
// Operator values as written in YAML, grouped for the picker. Labels come from t.operatorLabels.
const OPERATOR_GROUPS = [
  ['opGroupCompare', ['==', '!=', '>', '>=', '<', '<=', 'approx']],
  ['opGroupText', ['contains', 'not_contains', 'starts_with', 'ends_with', 'matches']],
  ['opGroupList', ['in', 'not_in']],
  [
    'opGroupPresence',
    ['exists', 'not_exists', 'is_null', 'is_missing', 'is_empty', 'is_not_empty', 'is_type'],
  ],
];
const NO_EXPECTED = new Set([
  'exists',
  'not_exists',
  'is_null',
  'is_missing',
  'is_empty',
  'is_not_empty',
]);
const VALUE_TYPES = ['string', 'number', 'boolean', 'array', 'object', 'null'];

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

function AssertionRow({ t, assertion, listId, aggregations, update, remove }) {
  const [showOptions, setShowOptions] = useState(false);
  const operator = assertion.operator;
  const hasOptions = Boolean(assertion.strict || assertion.caseSensitive || assertion.tolerance);
  return (
    <div className="assertion-item">
      <div className="assertion-row">
        <input
          className="form-control font-monospace"
          list={listId}
          value={assertion.expression}
          placeholder={t.assertionValue}
          aria-label={t.assertionCheck}
          spellCheck={false}
          onChange={(event) => update({ expression: event.target.value })}
        />
        <select
          className="form-select"
          aria-label={t.aggregation}
          title={t.aggregationHint}
          value={assertion.aggregate || ''}
          onChange={(event) => update({ aggregate: event.target.value })}
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
          value={operator}
          onChange={(event) => update({ operator: event.target.value })}
        >
          {OPERATOR_GROUPS.map(([group, operators]) => (
            <optgroup key={group} label={t[group]}>
              {operators.map((value) => (
                <option key={value} value={value}>
                  {t.operatorLabels?.[value] || value}
                </option>
              ))}
            </optgroup>
          ))}
        </select>
        {operator === 'is_type' ? (
          <select
            className="form-select"
            aria-label={t.expected}
            value={assertion.expected || 'string'}
            onChange={(event) => update({ expected: event.target.value })}
          >
            {VALUE_TYPES.map((type) => (
              <option key={type} value={type}>
                {t.valueTypes?.[type] || type}
              </option>
            ))}
          </select>
        ) : (
          <VariableInput
            value={NO_EXPECTED.has(operator) ? '' : assertion.expected}
            disabled={NO_EXPECTED.has(operator)}
            placeholder={
              NO_EXPECTED.has(operator)
                ? '—'
                : ({
                    in: t.expectedListPlaceholder,
                    not_in: t.expectedListPlaceholder,
                    matches: t.expectedRegexPlaceholder,
                  }[operator] ?? t.expected)
            }
            onChange={(event) => update({ expected: event.target.value })}
          />
        )}
        <button
          type="button"
          className={`btn-icon ${showOptions || hasOptions ? 'active' : ''}`}
          aria-label={t.assertionOptions}
          aria-expanded={showOptions}
          title={t.assertionOptions}
          onClick={() => setShowOptions(!showOptions)}
        >
          <Icon name="sliders" size={14} />
        </button>
        <button
          type="button"
          className="btn-icon danger"
          aria-label={t.remove}
          title={t.remove}
          onClick={remove}
        >
          <Icon name="x" size={14} />
        </button>
      </div>
      {showOptions && (
        <div className="assertion-options">
          <label className="check" title={t.caseSensitiveHint}>
            <input
              type="checkbox"
              checked={Boolean(assertion.caseSensitive)}
              onChange={(event) => update({ caseSensitive: event.target.checked })}
            />
            {t.caseSensitive}
          </label>
          <label className="check" title={t.strictTypesHint}>
            <input
              type="checkbox"
              checked={Boolean(assertion.strict)}
              onChange={(event) => update({ strict: event.target.checked })}
            />
            {t.strictTypes}
          </label>
          {operator === 'approx' && (
            <label className="check">
              {t.tolerance}
              <input
                className="form-control tolerance-input"
                inputMode="decimal"
                value={assertion.tolerance ?? ''}
                placeholder="0.01"
                onChange={(event) => update({ tolerance: event.target.value })}
              />
            </label>
          )}
        </div>
      )}
    </div>
  );
}

function AssertionsEditor({
  t,
  step,
  index,
  aggregations,
  savedNames,
  savedResponseNames,
  variableNames,
  addAssertion,
  updateAssertion,
  removeAssertion,
}) {
  // Everything an assertion can read: this step's own result, results saved by steps before it
  // (and by this step itself), and the collection / test variables.
  const sources = [
    ...(step.type === 'request'
      ? ['status', 'duration_ms', 'body', 'body_text', 'headers']
      : ['row_count', 'duration_ms', 'rows']
    ).map((name) => [name, t.sourceResult]),
    ...savedNames.map((name) => [name, t.sourceSaved]),
    // A saved response also exposes the HTTP response itself under @http, apart from its body fields.
    ...savedResponseNames.flatMap((name) => [
      [`${name}.@http.headers`, t.sourceSaved],
      [`${name}.@http.status`, t.sourceSaved],
      [`${name}.@http.duration_ms`, t.sourceSaved],
    ]),
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
            <span />
          </div>
          {step.assertions.map((assertion, assertionIndex) => (
            <AssertionRow
              key={assertionIndex}
              t={t}
              assertion={assertion}
              listId={listId}
              aggregations={aggregations}
              update={(patch) => updateAssertion(index, assertionIndex, patch)}
              remove={() => removeAssertion(index, assertionIndex)}
            />
          ))}
        </div>
      )}
      <button type="button" className="btn btn-link" onClick={() => addAssertion(index)}>
        <Icon name="plus" size={13} /> {t.addAssertion}
      </button>
    </section>
  );
}

function IncludePanel({ t, step, patch, sharedOptions }) {
  const item = sharedOptions.find((option) => option.id === step.ref);
  return (
    <>
      <Field label={t.sharedSteps}>
        <select
          className="form-select"
          value={step.ref}
          onChange={(event) => patch('ref', event.target.value)}
        >
          <option value="">{t.chooseShared}</option>
          {sharedOptions.map((option) => (
            <option key={option.id} value={option.id}>
              {option.name}
            </option>
          ))}
          {step.ref && !item && <option value={step.ref}>{step.ref}</option>}
        </select>
      </Field>
      {step.ref && !item && <p className="field-hint warn">{t.sharedMissing}</p>}
      {item && (
        <div className="include-info">
          {item.description && <p className="muted">{item.description}</p>}
          <div className="chip-row">
            <span className="chip">{item.run === 'once' ? t.runsOnce : t.runsEach}</span>
            <span className="chip">
              {item.stepCount} {item.stepCount === 1 ? t.stepOne : t.stepsShort}
            </span>
            {item.provides.map((name) => (
              <span className="chip mono" key={name} title={t.providesHint}>{`{{${name}}}`}</span>
            ))}
          </div>
          <p className="field-hint">{item.run === 'once' ? t.runsOnceHint : t.runsEachHint}</p>
        </div>
      )}
    </>
  );
}

function StepCard({
  t,
  step,
  index,
  total,
  open,
  toggle,
  preview,
  send,
  closePreview,
  updateStep,
  removeStep,
  moveStep,
  connectionNames,
  sharedOptions,
  ...assertionProps
}) {
  const [panel, setPanel] = useState('params');
  const patch = (key, value) => updateStep(index, { [key]: value });
  const isRequest = step.type === 'request';
  const supportsBody = isRequest && methodSupportsBody(step.method);
  const activePanel = panel === 'body' && !supportsBody ? 'params' : panel;
  const isInclude = step.type === 'include';
  const sharedItem = sharedOptions.find((option) => option.id === step.ref);
  let summary = (step.sql || '').split('\n')[0];
  if (isRequest) summary = step.url;
  if (isInclude) summary = sharedItem?.name || step.ref;

  return (
    <article className={`step-card ${open ? 'open' : ''}`}>
      <header className="step-head">
        <button type="button" className="step-toggle" aria-expanded={open} onClick={toggle}>
          <span className="step-number">{index + 1}</span>
          {isRequest && <MethodBadge method={step.method} />}
          {isInclude && <span className="method-badge method-shared">{t.sharedBadge}</span>}
          {!isRequest && !isInclude && <span className="method-badge method-sql">SQL</span>}
          <span className="step-title">
            {step.name || (isInclude ? sharedItem?.name : '') || t.untitledStep}
          </span>
          <span className="step-summary mono" title={summary}>
            {summary}
          </span>
          {!isInclude && step.assertions.length > 0 && (
            <span className="chip" title={t.assertions}>
              <Icon name="checkCircle" size={13} /> {step.assertions.length}
            </span>
          )}
          <Icon name={open ? 'chevronDown' : 'chevronRight'} size={15} className="step-chevron" />
        </button>
        <div className="step-tools">
          {!isInclude && (
            <button
              type="button"
              className="btn btn-ghost btn-sm send-btn"
              title={t.sendHint}
              disabled={preview?.loading}
              onClick={send}
            >
              <Icon
                name={preview?.loading ? 'spinner' : 'send'}
                size={13}
                className={preview?.loading ? 'spin' : ''}
              />{' '}
              {t.send}
            </button>
          )}
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
          {isInclude ? (
            <IncludePanel t={t} step={step} patch={patch} sharedOptions={sharedOptions} />
          ) : (
            <>
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

              {preview && (
                <ResponsePreview
                  t={t}
                  step={step}
                  index={index}
                  preview={preview}
                  close={closePreview}
                  addCheck={(assertion) => patch('assertions', [...step.assertions, assertion])}
                />
              )}

              <AssertionsEditor t={t} step={step} index={index} {...assertionProps} />
            </>
          )}
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
  kind = 'test',
  sharedList = [],
  runTest,
  running,
  previewStep,
}) {
  const isShared = kind === 'shared';
  // A group cannot include itself; every other group can be included.
  const sharedOptions = sharedList.filter((item) => !(isShared && item.fileName === fileName));
  const providedBy = (step) => [
    step.save_as,
    ...(step.type === 'include'
      ? (sharedOptions.find((item) => item.id === step.ref)?.provides ?? [])
      : []),
  ];
  const [open, setOpen] = useState(
    () => new Set(test.steps.length <= 3 ? test.steps.map((_, i) => i) : [0]),
  );
  const [showYaml, setShowYaml] = useState(false);
  // What each step received when it was last sent, by step index.
  const [previews, setPreviews] = useState({});
  const previousCount = useRef(test.steps.length);

  // A newly added step opens itself; a removed one shifts the open indexes.
  useEffect(() => {
    if (test.steps.length > previousCount.current) {
      setOpen((current) => new Set([...current, test.steps.length - 1]));
    }
    previousCount.current = test.steps.length;
  }, [test.steps.length]);

  // Runs the steps up to `index` as they are in the editor and shows what that step received.
  const send = async (index) => {
    setOpen((current) => new Set([...current, index]));
    setPreviews((current) => ({ ...current, [index]: { loading: true } }));
    let next;
    try {
      next = { data: await previewStep(index) };
    } catch (error) {
      next = { error: error.message };
    }
    setPreviews((current) => ({ ...current, [index]: next }));
  };
  const closePreview = (index) =>
    setPreviews((current) => {
      const next = { ...current };
      delete next[index];
      return next;
    });

  const remove = (index) => {
    // Previews belong to step positions, which just changed.
    setPreviews({});
    setOpen(
      (current) =>
        new Set([...current].filter((i) => i !== index).map((i) => (i > index ? i - 1 : i))),
    );
    removeStep(index);
  };
  const move = (index, direction) => {
    setPreviews({});
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
  const canSave = test.name.trim() && (isShared || test.endpoint.trim());

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
        {!isShared && (
          <button
            type="button"
            className="btn btn-ghost btn-sm"
            disabled={!runTest || running}
            title={runTest ? t.runTest : t.runTestSaveFirst}
            onClick={runTest}
          >
            <Icon name="play" size={14} /> {t.runTest}
          </button>
        )}
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
          placeholder={isShared ? t.sharedNamePlaceholder : t.testNamePlaceholder}
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
        {isShared ? (
          <>
            <div className="endpoint-line">
              <select
                className="form-select run-select"
                aria-label={t.runMode}
                value={test.run}
                onChange={(event) => setTest({ ...test, run: event.target.value })}
              >
                <option value="each">{t.runsEach}</option>
                <option value="once">{t.runsOnce}</option>
              </select>
            </div>
            <p className="field-hint">{test.run === 'once' ? t.runsOnceHint : t.runsEachHint}</p>
          </>
        ) : (
          <>
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
          </>
        )}
        <p className="field-hint file-hint" title={isShared ? t.sharedFileHint : t.fileHint}>
          <Icon name="code" size={12} />
          <span className="mono">
            {fileName ? `${isShared ? 'shared' : 'tests'}/${fileName}` : t.newFileHint}
          </span>
        </p>
        {!canSave && dirty && (
          <p className="field-hint warn">
            {isShared ? t.nameRequiredShared : t.nameAndEndpointRequired}
          </p>
        )}
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
            preview={previews[index]}
            send={() => send(index)}
            closePreview={() => closePreview(index)}
            updateStep={updateStep}
            removeStep={remove}
            moveStep={move}
            connectionNames={connectionNames}
            sharedOptions={sharedOptions}
            aggregations={aggregations}
            addAssertion={addAssertion}
            updateAssertion={updateAssertion}
            removeAssertion={removeAssertion}
            savedNames={test.steps
              .slice(0, index + 1)
              .flatMap(providedBy)
              .filter(Boolean)}
            savedResponseNames={test.steps
              .slice(0, index + 1)
              .filter((previous) => previous.type === 'request')
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
            <button
              type="button"
              className="btn btn-dashed"
              disabled={sharedOptions.length === 0}
              title={sharedOptions.length === 0 ? t.noSharedYet : t.sharedStepButtonHint}
              onClick={() => addStep('include')}
            >
              <Icon name="swap" size={15} /> {t.sharedStepButton}
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
