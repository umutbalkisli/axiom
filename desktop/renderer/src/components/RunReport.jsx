import React, { useState } from 'react';
import { MethodBadge } from './Badge.jsx';
import EmptyState from './EmptyState.jsx';
import Icon from './Icons.jsx';

function formatDuration(ms) {
  if (ms == null || Number.isNaN(ms)) return '';
  return ms >= 1000 ? `${(ms / 1000).toFixed(2)} s` : `${Math.round(ms)} ms`;
}

function formatValue(value) {
  if (value === null || value === undefined) return 'null';
  const text = typeof value === 'string' ? value : JSON.stringify(value);
  return text.length > 200 ? `${text.slice(0, 200)}…` : text;
}

const OUTCOME_ICON = { passed: 'checkCircle', failed: 'xCircle', error: 'alert' };
const OUTCOME_CLASS = { passed: 'text-success', failed: 'text-danger', error: 'text-warn' };

function StatusIcon({ outcome }) {
  return <Icon name={OUTCOME_ICON[outcome]} size={16} className={OUTCOME_CLASS[outcome]} />;
}

function AssertionLine({ t, assertion }) {
  const { outcome } = assertion;
  return (
    <li className={`assertion-line ${outcome}`}>
      <StatusIcon outcome={outcome} />
      <div className="assertion-text">
        <span className="mono">{assertion.text}</span>
        {outcome === 'failed' && (
          <div className="assertion-fail">
            {assertion.error && !assertion.error.startsWith("Expected '") && (
              <div>{assertion.error}</div>
            )}
            <div className="compare">
              <span>
                {t.expectedLabel}: <code>{formatValue(assertion.expected)}</code>
              </span>
              <span>
                {t.actualLabel}: <code>{formatValue(assertion.actual)}</code>
              </span>
            </div>
            {assertion.error?.endsWith('was not found)') && (
              <div className="muted-note">
                {assertion.error.slice(assertion.error.lastIndexOf('('))}
              </div>
            )}
          </div>
        )}
        {outcome === 'error' && (
          <div className="assertion-error">
            <strong>{t.couldNotEvaluate}</strong>
            <div>{assertion.error}</div>
          </div>
        )}
      </div>
      {outcome === 'passed' && assertion.actual !== undefined && (
        <span className="mono muted actual">{formatValue(assertion.actual)}</span>
      )}
    </li>
  );
}

function StepResult({ t, step }) {
  return (
    <div className="result-step">
      <div className="result-step-head">
        <StatusIcon outcome={step.outcome} />
        <strong>{step.name}</strong>
        <span className="chip">
          {step.type === 'db_query' ? 'SQL' : step.type === 'include' ? t.sharedBadge : step.type}
        </span>
        {step.statusCode != null && (
          <span className="chip">
            {t.status} {step.statusCode}
          </span>
        )}
        {step.rowCount != null && (
          <span className="chip">
            {step.rowCount} {t.rows}
          </span>
        )}
        <span className="spacer" />
        <span className="mono muted">{formatDuration(step.durationMs)}</span>
      </div>
      {step.error && (
        <div className="error-box">
          <strong>{t.stepCouldNotRun}</strong>
          <div>{step.error}</div>
        </div>
      )}
      {step.assertions.length > 0 && (
        <ul className="assertion-list">
          {step.assertions.map((assertion, i) => (
            <AssertionLine key={i} t={t} assertion={assertion} />
          ))}
        </ul>
      )}
      {step.children?.length > 0 && (
        <div className="result-children">
          {step.children.map((child, i) => (
            <StepResult key={i} t={t} step={child} />
          ))}
        </div>
      )}
    </div>
  );
}

function TestRow({ t, test, open, toggle }) {
  return (
    <div className={`result-row ${test.outcome} ${open ? 'open' : ''}`}>
      <button type="button" className="result-head" aria-expanded={open} onClick={toggle}>
        <StatusIcon outcome={test.outcome} />
        {test.method ? <MethodBadge method={test.method} /> : null}
        <span className="result-name">{test.name}</span>
        {test.endpoint && <span className="mono muted result-endpoint">{test.endpoint}</span>}
        <span className="spacer" />
        <span className="mono muted">{formatDuration(test.durationMs)}</span>
        <Icon name={open ? 'chevronDown' : 'chevronRight'} size={15} className="step-chevron" />
      </button>
      {open && (
        <div className="result-body">
          {test.steps.length === 0 && <p className="muted small">{t.noSteps}</p>}
          {test.steps.map((step, index) => (
            <StepResult key={index} t={t} step={step} />
          ))}
        </div>
      )}
    </div>
  );
}

// While a run is going: a progress bar, each test as it finishes, and a way to stop.
function RunProgress({ t, progress, cancelRun }) {
  const finished = progress?.tests || [];
  const total = progress?.total;
  const outcomes = finished.map((test) => String(test.outcome || '').toLowerCase());
  const pct = (outcome) =>
    total ? (outcomes.filter((item) => item === outcome).length / total) * 100 : 0;
  return (
    <div className="page">
      <div className="run-banner running">
        <Icon name="spinner" size={22} className="spin" />
        <div className="run-banner-main">
          <h2>{t.running}</h2>
          <p aria-live="polite">
            {total == null
              ? t.runningSub
              : t.progressCount.replace('{done}', finished.length).replace('{total}', total)}
          </p>
          <div
            className="progress-bar"
            role="progressbar"
            aria-valuemin={0}
            aria-valuemax={total || 0}
            aria-valuenow={finished.length}
          >
            <span className="ok" style={{ width: `${pct('passed')}%` }} />
            <span className="bad" style={{ width: `${pct('failed')}%` }} />
            <span className="warn" style={{ width: `${pct('error')}%` }} />
          </div>
        </div>
        <button type="button" className="btn btn-secondary" onClick={cancelRun}>
          <Icon name="stop" size={14} /> {t.cancelRun}
        </button>
      </div>
      {finished.length > 0 && (
        <ul className="live-results">
          {finished.map((test, index) => (
            <li key={`${test.sourceFile}-${index}`}>
              <StatusIcon outcome={outcomes[index]} />
              <span className="result-name">{test.name}</span>
              <span className="spacer" />
              <span className="mono muted">
                {formatDuration(new Date(test.completedAt) - new Date(test.startedAt))}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

export default function RunReport({
  t,
  report,
  running,
  progress,
  rawOutput,
  run,
  runAll,
  rerunFailed,
  cancelRun,
  environment,
}) {
  const [filter, setFilter] = useState('all');
  const [rawOpen, setRawOpen] = useState(false);
  const [expanded, setExpanded] = useState(
    () =>
      new Set((report?.tests || []).flatMap((test, i) => (test.outcome === 'passed' ? [] : [i]))),
  );

  if (running) {
    return <RunProgress t={t} progress={progress} cancelRun={cancelRun} />;
  }

  if (!report) {
    return (
      <div className="page">
        <EmptyState
          icon="play"
          title={t.noRunTitle}
          description={t.noRun}
          action={
            <button type="button" className="btn btn-primary" onClick={run}>
              <Icon name="play" size={14} /> {t.run}
            </button>
          }
        />
      </div>
    );
  }

  if (report.error) {
    return (
      <div className="page">
        <div className="run-banner fail">
          <Icon name="xCircle" size={26} className="text-danger" />
          <div className="run-banner-main">
            <h2>{t.runFailedTitle}</h2>
            <pre className="plain-pre">{report.error}</pre>
          </div>
          <button type="button" className="btn btn-secondary" onClick={run}>
            <Icon name="play" size={14} /> {t.runAgain}
          </button>
        </div>
      </div>
    );
  }

  const allPassed = report.failed === 0 && report.errors === 0;
  let bannerTone = allPassed ? 'pass' : report.failed === 0 ? 'warn' : 'fail';
  if (report.cancelled) bannerTone = 'warn';
  const visible = report.tests
    .map((test, index) => ({ test, index }))
    .filter(({ test }) => filter === 'all' || test.outcome === filter);
  const toggle = (index) =>
    setExpanded((current) => {
      const next = new Set(current);
      if (next.has(index)) next.delete(index);
      else next.add(index);
      return next;
    });
  const share = (count) => (report.total ? (count / report.total) * 100 : 0);
  const passedPct = share(report.passed);
  const failedPct = share(report.failed);
  const errorPct = share(report.errors);
  const fill = (text) =>
    text
      .replace('{failed}', report.failed)
      .replace('{errors}', report.errors)
      .replace('{total}', report.total);
  let headline = t.allPassed;
  if (!allPassed) {
    if (report.errors === 0) headline = fill(t.someFailed);
    else if (report.failed === 0) headline = fill(t.someErrors);
    else headline = fill(t.failedAndErrors);
  }
  if (report.cancelled) headline = t.runCancelled;
  const notes = [
    report.cancelled &&
      t.runCancelledSub
        .replace('{done}', report.cancelled.done)
        .replace('{total}', report.cancelled.total),
    report.scope && t.ranSubset.replace('{count}', report.scope.length),
  ].filter(Boolean);

  return (
    <div className="page">
      <div className={`run-banner ${bannerTone}`}>
        <StatusIcon
          outcome={
            allPassed && !report.cancelled
              ? 'passed'
              : report.failed === 0 || report.cancelled
                ? 'error'
                : 'failed'
          }
        />
        <div className="run-banner-main">
          <h2>{headline}</h2>
          <p>
            {formatDuration(report.durationMs)}
            {environment ? ` · ${t.environment}: ${environment}` : ''}
            {report.completedAt ? ` · ${report.completedAt}` : ''}
          </p>
          {notes.map((note) => (
            <p key={note}>{note}</p>
          ))}
          <div className="progress-bar" aria-hidden="true">
            <span className="ok" style={{ width: `${passedPct}%` }} />
            <span className="bad" style={{ width: `${failedPct}%` }} />
            <span className="warn" style={{ width: `${errorPct}%` }} />
          </div>
        </div>
        <div className="run-banner-actions">
          {!allPassed && (
            <button type="button" className="btn btn-secondary" onClick={rerunFailed}>
              <Icon name="refresh" size={14} /> {t.runFailedAgain}
            </button>
          )}
          <button type="button" className="btn btn-secondary" onClick={run}>
            <Icon name="play" size={14} /> {t.runAgain}
          </button>
          {report.scope && (
            <button type="button" className="btn btn-ghost" onClick={runAll}>
              {t.run}
            </button>
          )}
        </div>
      </div>

      <div className="stat-row">
        <div className="stat">
          <span className="stat-num">{report.total}</span>
          <span className="stat-label">{t.total}</span>
        </div>
        <div className="stat success">
          <span className="stat-num">{report.passed}</span>
          <span className="stat-label">{t.passed}</span>
        </div>
        <div className="stat danger">
          <span className="stat-num">{report.failed}</span>
          <span className="stat-label">{t.failed}</span>
        </div>
        {report.errors > 0 && (
          <div className="stat warn" title={t.errorsHint}>
            <span className="stat-num">{report.errors}</span>
            <span className="stat-label">{t.couldNotEvaluatePlural}</span>
          </div>
        )}
        <div className="stat">
          <span className="stat-num">{report.rate}%</span>
          <span className="stat-label">{t.success}</span>
        </div>
      </div>

      <div className="section-head">
        <h3>{t.testResults}</h3>
        <div className="segmented" role="group" aria-label={t.filter}>
          {[
            ['all', `${t.filterAll} ${report.total}`],
            ['failed', `${t.failed} ${report.failed}`],
            ...(report.errors > 0 ? [['error', `${t.errorsShort} ${report.errors}`]] : []),
            ['passed', `${t.passed} ${report.passed}`],
          ].map(([value, label]) => (
            <button
              type="button"
              key={value}
              className={filter === value ? 'active' : ''}
              onClick={() => setFilter(value)}
            >
              {label}
            </button>
          ))}
        </div>
      </div>

      <div className="result-list">
        {visible.length ? (
          visible.map(({ test, index }) => (
            <TestRow
              key={index}
              t={t}
              test={test}
              open={expanded.has(index)}
              toggle={() => toggle(index)}
            />
          ))
        ) : (
          <p className="muted">{report.total ? t.noMatches : t.emptyOutput}</p>
        )}
      </div>

      <section className="raw-panel">
        <button type="button" className="btn btn-link" onClick={() => setRawOpen(!rawOpen)}>
          <Icon name={rawOpen ? 'chevronDown' : 'chevronRight'} size={13} /> {t.raw}
        </button>
        {rawOpen && <pre>{rawOutput || t.noOutput}</pre>}
      </section>
    </div>
  );
}
