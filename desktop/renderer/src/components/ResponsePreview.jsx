import React, { useMemo, useState } from 'react';
import Icon from './Icons.jsx';

const MAX_CHILDREN = 100;

function formatDuration(ms) {
  if (ms == null || Number.isNaN(ms)) return '';
  return ms >= 1000 ? `${(ms / 1000).toFixed(2)} s` : `${Math.round(ms)} ms`;
}

// A check the builder can add, in the builder's own assertion shape.
function check(expression, operator, expected = '', aggregate = '') {
  return {
    expression,
    aggregate,
    strict: false,
    caseSensitive: false,
    tolerance: '',
    operator,
    expected,
  };
}

// "value == x" for a value picked in a response; a null value is checked with is_null.
function checkFor(expression, value) {
  if (value === null || value === undefined) return check(expression, 'is_null');
  return check(expression, '==', typeof value === 'string' ? value : JSON.stringify(value));
}

function valueClass(value) {
  if (value === null) return 'json-null';
  return `json-${typeof value}`;
}

function JsonNode({ t, name, value, path, depth, pick }) {
  const [open, setOpen] = useState(depth < 2);
  const [showAll, setShowAll] = useState(false);
  const expression = path ? `body.${path}` : 'body';
  const isArray = Array.isArray(value);
  const label = name != null && <span className="json-key">{name}: </span>;

  if (value === null || typeof value !== 'object') {
    return (
      <div className="json-row" style={{ paddingLeft: depth * 14 }}>
        {label}
        {pick ? (
          <button
            type="button"
            className={`json-value ${valueClass(value)}`}
            title={`${expression} == ${JSON.stringify(value)}`}
            onClick={() => pick(checkFor(expression, value))}
          >
            {JSON.stringify(value)}
          </button>
        ) : (
          <span className={`json-value ${valueClass(value)}`}>{JSON.stringify(value)}</span>
        )}
      </div>
    );
  }

  const entries = isArray ? value.map((item, i) => [i, item]) : Object.entries(value);
  const visible = showAll ? entries : entries.slice(0, MAX_CHILDREN);
  return (
    <>
      <div className="json-row" style={{ paddingLeft: depth * 14 }}>
        <button
          type="button"
          className="json-toggle"
          aria-expanded={open}
          onClick={() => setOpen(!open)}
        >
          <Icon name={open ? 'chevronDown' : 'chevronRight'} size={12} />
          {label}
          <span className="muted">{isArray ? `[${entries.length}]` : `{${entries.length}}`}</span>
        </button>
        {pick && isArray && (
          <button
            type="button"
            className="json-action"
            title={`${expression} count == ${entries.length}`}
            onClick={() => pick(check(expression, '==', String(entries.length), 'count'))}
          >
            {t.countCheck}
          </button>
        )}
      </div>
      {open &&
        visible.map(([key, child]) => (
          <JsonNode
            key={key}
            t={t}
            name={key}
            value={child}
            path={path ? `${path}.${key}` : String(key)}
            depth={depth + 1}
            pick={pick}
          />
        ))}
      {open && !showAll && entries.length > MAX_CHILDREN && (
        <div className="json-row" style={{ paddingLeft: (depth + 1) * 14 }}>
          <button type="button" className="btn btn-link" onClick={() => setShowAll(true)}>
            … {entries.length - MAX_CHILDREN}
          </button>
        </div>
      )}
    </>
  );
}

function Body({ t, response, pick }) {
  const parsed = useMemo(() => {
    if (response.bodyTruncated) return undefined;
    try {
      return JSON.parse(response.body);
    } catch {
      return undefined;
    }
  }, [response.body, response.bodyTruncated]);

  if (!response.body) return <p className="muted small">—</p>;
  return (
    <>
      {response.bodyTruncated && <p className="field-hint warn">{t.responseTruncated}</p>}
      {parsed === undefined ? (
        <pre className="response-text">{response.body}</pre>
      ) : (
        <div className="json-tree">
          <JsonNode t={t} name={null} value={parsed} path="" depth={0} pick={pick} />
        </div>
      )}
    </>
  );
}

function Rows({ t, response, pick }) {
  const rows = response.rows || [];
  const columns = [...new Set(rows.flatMap((row) => Object.keys(row)))];
  if (!rows.length) return <p className="muted small">0 {t.rows}</p>;
  return (
    <>
      {response.rowCount > rows.length && (
        <p className="field-hint">
          {t.rowsShown.replace('{shown}', rows.length).replace('{total}', response.rowCount)}
        </p>
      )}
      <div className="rows-table-wrap">
        <table className="rows-table">
          <thead>
            <tr>
              <th>#</th>
              {columns.map((column) => (
                <th key={column}>{column}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, i) => (
              <tr key={i}>
                <td className="muted">{i}</td>
                {columns.map((column) => (
                  <td key={column}>
                    {pick ? (
                      <button
                        type="button"
                        className={`json-value ${valueClass(row[column] ?? null)}`}
                        onClick={() => pick(checkFor(`rows.${i}.${column}`, row[column] ?? null))}
                      >
                        {row[column] === null ? 'null' : String(row[column])}
                      </button>
                    ) : (
                      String(row[column] ?? 'null')
                    )}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}

const shellQuote = (value) => `'${String(value).replaceAll("'", `'\\''`)}'`;

// The request as a curl command, to reproduce it outside Axiom. Secret values stay masked.
function toCurl(request) {
  return [
    'curl',
    '-X',
    request.method,
    shellQuote(request.url),
    ...Object.entries(request.headers || {}).flatMap(([name, value]) => [
      '-H',
      shellQuote(`${name}: ${value}`),
    ]),
    ...(request.body != null ? ['--data-raw', shellQuote(request.body)] : []),
  ].join(' ');
}

function prettyBody(body) {
  try {
    return JSON.stringify(JSON.parse(body), null, 2);
  } catch {
    return body;
  }
}

// What the step sent: always the method and URL (or the SQL); headers and body on demand. Opens by
// itself when the call failed, because that is when the resolved request explains the problem.
function Sent({ t, response, failed }) {
  const [open, setOpen] = useState(failed);
  const [copied, setCopied] = useState(false);
  const { request, sql } = response;
  if (!request && !sql) return null;

  const copy = async () => {
    await navigator.clipboard.writeText(toCurl(request));
    setCopied(true);
    setTimeout(() => setCopied(false), 1800);
  };

  return (
    <div className="sent">
      <div className="sent-head">
        <button
          type="button"
          className="json-toggle sent-toggle"
          aria-expanded={open}
          onClick={() => setOpen(!open)}
        >
          <Icon name={open ? 'chevronDown' : 'chevronRight'} size={12} />
          <span className="muted">{request ? t.sentRequest : t.sentSql}</span>
        </button>
        {request && (
          <span className="mono sent-line" title={request.url}>
            <strong>{request.method}</strong> {request.url}
          </span>
        )}
        {request && (
          <button type="button" className="json-action" title={t.copyCurlHint} onClick={copy}>
            <Icon name={copied ? 'check' : 'copy'} size={12} /> {copied ? t.copied : t.copyCurl}
          </button>
        )}
      </div>
      {open && sql != null && <pre className="response-text">{sql}</pre>}
      {open && request && (
        <>
          <div className="json-tree">
            {Object.entries(request.headers || {}).map(([name, value]) => (
              <div className="json-row" key={name}>
                <span className="json-key">{name}: </span>
                <span className="json-value json-string">{value}</span>
              </div>
            ))}
          </div>
          {request.bodyTruncated && <p className="field-hint warn">{t.responseTruncated}</p>}
          {request.body != null ? (
            <pre className="response-text">{prettyBody(request.body)}</pre>
          ) : (
            <p className="muted small">{t.noRequestBody}</p>
          )}
        </>
      )}
    </div>
  );
}

function describe(assertion) {
  const target = `${assertion.source}${assertion.path ? `.${assertion.path}` : ''}`;
  const subject = assertion.aggregate ? `${assertion.aggregate}(${target})` : target;
  return `${subject} ${assertion.operator} ${assertion.expected ?? ''}`.trim();
}

const OUTCOME_ICON = { passed: 'checkCircle', failed: 'xCircle', error: 'alert' };
const OUTCOME_CLASS = { passed: 'text-success', failed: 'text-danger', error: 'text-warn' };
const outcomeOf = (item) => String(item?.outcome || '').toLowerCase();

/**
 * What a step received when it was sent from the builder: its checks, the response (or rows), and
 * one-click checks for any value in it.
 */
export default function ResponsePreview({ t, step, index, preview, close, addCheck }) {
  const [showHeaders, setShowHeaders] = useState(false);

  let content;
  if (preview.loading) {
    content = (
      <p className="muted">
        <Icon name="spinner" size={14} className="spin" /> {t.sending}
      </p>
    );
  } else if (preview.error) {
    content = (
      <div className="error-box">
        <strong>{t.stepCouldNotRun}</strong>
        <div>{preview.error}</div>
      </div>
    );
  } else {
    const { result, response } = preview.data;
    const steps = result?.steps || [];
    const last = steps[steps.length - 1];
    const reached = steps.length === index + 1;
    // Checks only make sense on this step's own response.
    const pick = reached && response?.stepId === step.id ? addCheck : null;
    content = (
      <>
        {!reached && last && (
          <div className="error-box">
            <strong>
              {t.stoppedEarlier.replace('{step}', steps.length).replace('{name}', last.name)}
            </strong>
            {last.error && <div>{last.error}</div>}
          </div>
        )}
        {last?.error && reached && (
          <div className="error-box">
            <strong>{t.stepCouldNotRun}</strong>
            <div>{last.error}</div>
          </div>
        )}
        {(last?.assertions || []).length > 0 && (
          <ul className="assertion-list compact">
            {last.assertions.map((assertion, i) => (
              <li key={i} className={`assertion-line ${outcomeOf(assertion)}`}>
                <Icon
                  name={OUTCOME_ICON[outcomeOf(assertion)]}
                  size={14}
                  className={OUTCOME_CLASS[outcomeOf(assertion)]}
                />
                <span className="mono">{describe(assertion)}</span>
                {outcomeOf(assertion) !== 'passed' && assertion.error && (
                  <span className="muted small">{assertion.error}</span>
                )}
              </li>
            ))}
          </ul>
        )}
        {!response && !last?.error && <p className="muted small">{t.noResponse}</p>}
        {response && (
          <Sent
            key={`${response.stepId}-${response.status ?? 'none'}`}
            t={t}
            response={response}
            failed={response.status == null || response.status >= 400 || Boolean(last?.error)}
          />
        )}
        {response && (response.status != null || response.rows) && (
          <>
            <div className="response-meta">
              {response.status != null &&
                (pick ? (
                  <button
                    type="button"
                    className={`chip chip-button ${response.status >= 400 ? 'chip-bad' : ''}`}
                    title={`status == ${response.status}`}
                    onClick={() => pick(check('status', '==', String(response.status)))}
                  >
                    {t.status} {response.status}
                  </button>
                ) : (
                  <span className={`chip ${response.status >= 400 ? 'chip-bad' : ''}`}>
                    {t.status} {response.status}
                  </span>
                ))}
              {response.rowCount != null &&
                (pick ? (
                  <button
                    type="button"
                    className="chip chip-button"
                    title={`row_count == ${response.rowCount}`}
                    onClick={() => pick(check('row_count', '==', String(response.rowCount)))}
                  >
                    {response.rowCount} {t.rows}
                  </button>
                ) : (
                  <span className="chip">
                    {response.rowCount} {t.rows}
                  </span>
                ))}
              <span className="mono muted">{formatDuration(response.durationMs)}</span>
              {response.headers && (
                <button
                  type="button"
                  className="btn btn-link"
                  aria-expanded={showHeaders}
                  onClick={() => setShowHeaders(!showHeaders)}
                >
                  <Icon name={showHeaders ? 'chevronDown' : 'chevronRight'} size={12} />{' '}
                  {t.responseHeaders} ({Object.keys(response.headers).length})
                </button>
              )}
              {pick && <span className="muted small spacer-left">{t.clickToAssert}</span>}
            </div>
            {showHeaders && response.headers && (
              <div className="json-tree">
                {Object.entries(response.headers).map(([name, value]) => (
                  <div className="json-row" key={name}>
                    <span className="json-key">{name}: </span>
                    {pick ? (
                      <button
                        type="button"
                        className="json-value json-string"
                        onClick={() => pick(check(`headers.${name}`, '==', value))}
                      >
                        {value}
                      </button>
                    ) : (
                      <span className="json-value json-string">{value}</span>
                    )}
                  </div>
                ))}
              </div>
            )}
            {response.rows ? (
              <Rows t={t} response={response} pick={pick} />
            ) : (
              <Body t={t} response={response} pick={pick} />
            )}
          </>
        )}
      </>
    );
  }

  return (
    <section className="response-panel" aria-label={t.response}>
      <div className="section-head">
        <h4>{t.response}</h4>
        <button
          type="button"
          className="btn-icon"
          aria-label={t.closePreview}
          title={t.closePreview}
          onClick={close}
        >
          <Icon name="x" size={14} />
        </button>
      </div>
      {content}
    </section>
  );
}
