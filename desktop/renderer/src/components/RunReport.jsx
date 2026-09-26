import React from 'react';
import { MethodBadge, StatusBadge, splitEndpointLabel } from './Badge.jsx';

function Stat({ label, value, tone }) {
  return (
    <article className={`axiom-stat ${tone ? `tone-${tone}` : ''}`}>
      <div className="axiom-stat-num">{value}</div>
      <div className="axiom-stat-label">{label}</div>
    </article>
  );
}

export default function RunReport({ t, report, rawOutput, rawOpen, setRawOpen }) {
  const badge = getBadge(report, t);
  return (
    <section>
      <div className="row row-cols-2 row-cols-lg-4 g-3 mb-4">
        <div className="col">
          <Stat label={t.total} value={report?.total ?? 0} />
        </div>
        <div className="col">
          <Stat label={t.passed} value={report?.passed ?? '-'} tone="success" />
        </div>
        <div className="col">
          <Stat label={t.failed} value={report?.failed ?? '-'} tone="danger" />
        </div>
        <div className="col">
          <Stat label={t.success} value={report?.rate ? `${report.rate}%` : '-'} />
        </div>
      </div>
      <section className="card mb-3">
        <div className="card-header d-flex justify-content-between align-items-center">
          <span>{t.runSummary}</span>
          <span className={`status-badge ${badge.tone}`}>
            <span className="status-dot" />
            {badge.label}
          </span>
        </div>
        <div className="card-body">
          {report ? (
            <>
              <p>
                {t.total}: <strong>{report.total}</strong>
              </p>
              <p>
                {t.passed}: <strong>{report.passed}</strong> | {t.failed}:{' '}
                <strong>{report.failed}</strong>
              </p>
              <p className="mb-0">
                {t.success}: <strong>{report.rate}%</strong>
              </p>
            </>
          ) : (
            <p className="mb-0 text-secondary">{t.noRun}</p>
          )}
        </div>
      </section>
      <section className="card mb-3">
        <div className="card-header d-flex justify-content-between align-items-center">
          <span>{t.raw}</span>
          <button className="btn btn-sm btn-outline-secondary" onClick={() => setRawOpen(!rawOpen)}>
            {rawOpen ? t.hide : t.show}
          </button>
        </div>
        {rawOpen && (
          <pre className="card-body mb-0 text-light">{rawOutput || t.noOutput}</pre>
        )}
      </section>
      <section className="card">
        <div className="card-header">{t.testResults}</div>
        <div className="table-responsive">
          <table className="table table-hover mb-0">
            <thead>
              <tr>
                <th>{t.scenario}</th>
                <th>{t.status}</th>
                <th>{t.duration}</th>
              </tr>
            </thead>
            <tbody>
              {report?.groups?.length ? (
                report.groups.map((group) => {
                  const { method, path } = splitEndpointLabel(group.label);
                  return (
                    <React.Fragment key={group.label}>
                      <tr className="table-light">
                        <th colSpan="3">
                          <span className="axiom-group-label">
                            {method ? <MethodBadge method={method} /> : null}
                            <span className="axiom-mono">{path}</span>
                          </span>
                        </th>
                      </tr>
                      {group.items.map((item, index) => (
                        <tr key={`${group.label}-${item.name}-${index}`}>
                          <td className="ps-4">{item.name}</td>
                          <td>
                            <StatusBadge tone={item.status === 'PASS' ? 'pass' : 'fail'}>
                              {item.status === 'PASS' ? t.passed : t.failed}
                            </StatusBadge>
                          </td>
                          <td className="axiom-mono">{item.duration} ms</td>
                        </tr>
                      ))}
                    </React.Fragment>
                  );
                })
              ) : (
                <tr>
                  <td colSpan="3" className="text-secondary">
                    {report ? t.emptyOutput : t.tableHint}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </section>
  );
}

function getBadge(report, t) {
  if (!report) return { tone: 'status-neutral', label: t.notRun };
  if (report.failed) return { tone: 'status-fail', label: t.failed };
  return { tone: 'status-pass', label: t.passed };
}
