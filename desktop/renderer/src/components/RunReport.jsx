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

function StatusIcon({ passed }) {
  return (
    <Icon
      name={passed ? 'checkCircle' : 'xCircle'}
      size={16}
      className={passed ? 'text-success' : 'text-danger'}
    />
  );
}

function AssertionLine({ t, assertion }) {
  return (
    <li className={`assertion-line ${assertion.passed ? 'pass' : 'fail'}`}>
      <StatusIcon passed={assertion.passed} />
      <div className="assertion-text">
        <span className="mono">{assertion.text}</span>
        {!assertion.passed && (
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
          </div>
        )}
      </div>
      {assertion.passed && assertion.actual !== undefined && (
        <span className="mono muted actual">{formatValue(assertion.actual)}</span>
      )}
    </li>
  );
}

function TestRow({ t, test, open, toggle }) {
  return (
    <div className={`result-row ${test.passed ? 'pass' : 'fail'} ${open ? 'open' : ''}`}>
      <button type="button" className="result-head" aria-expanded={open} onClick={toggle}>
        <StatusIcon passed={test.passed} />
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
            <div className="result-step" key={index}>
              <div className="result-step-head">
                <StatusIcon passed={step.passed} />
                <strong>{step.name}</strong>
                <span className="chip">{step.type === 'db_query' ? 'SQL' : step.type}</span>
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
              {step.error && <div className="error-box">{step.error}</div>}
              {step.assertions.length > 0 && (
                <ul className="assertion-list">
                  {step.assertions.map((assertion, i) => (
                    <AssertionLine key={i} t={t} assertion={assertion} />
                  ))}
                </ul>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export default function RunReport({ t, report, running, rawOutput, run, environment }) {
  const [filter, setFilter] = useState('all');
  const [rawOpen, setRawOpen] = useState(false);
  const [expanded, setExpanded] = useState(
    () => new Set((report?.tests || []).flatMap((test, i) => (test.passed ? [] : [i]))),
  );

  if (running) {
    return (
      <div className="page">
        <div className="run-banner running">
          <Icon name="spinner" size={22} className="spin" />
          <div>
            <h2>{t.running}</h2>
            <p>{t.runningSub}</p>
          </div>
        </div>
      </div>
    );
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

  const allPassed = report.failed === 0;
  const visible = report.tests
    .map((test, index) => ({ test, index }))
    .filter(({ test }) => filter === 'all' || (filter === 'failed' ? !test.passed : test.passed));
  const toggle = (index) =>
    setExpanded((current) => {
      const next = new Set(current);
      if (next.has(index)) next.delete(index);
      else next.add(index);
      return next;
    });
  const passedPct = report.total ? (report.passed / report.total) * 100 : 0;
  const headline = allPassed
    ? t.allPassed
    : t.someFailed.replace('{failed}', report.failed).replace('{total}', report.total);

  return (
    <div className="page">
      <div className={`run-banner ${allPassed ? 'pass' : 'fail'}`}>
        <StatusIcon passed={allPassed} />
        <div className="run-banner-main">
          <h2>{headline}</h2>
          <p>
            {formatDuration(report.durationMs)}
            {environment ? ` · ${t.environment}: ${environment}` : ''}
            {report.completedAt ? ` · ${report.completedAt}` : ''}
          </p>
          <div className="progress-bar" aria-hidden="true">
            <span className="ok" style={{ width: `${passedPct}%` }} />
            <span className="bad" style={{ width: `${100 - passedPct}%` }} />
          </div>
        </div>
        <button type="button" className="btn btn-secondary" onClick={run}>
          <Icon name="play" size={14} /> {t.runAgain}
        </button>
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
