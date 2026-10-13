/* GMC v2 console — Opexminds DS. Vanilla JS, no dependencies. Every number from the API. */
(() => {
'use strict';

// ── utilities ──────────────────────────────────────────────────────────────
const $ = (sel, el = document) => el.querySelector(sel);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&','<':'<','>':'>','"':'"',"'":'&#39;'}[c]));
const inr = (n) => n == null ? '—' : '₹' + Number(n).toLocaleString('en-IN', { maximumFractionDigits: 0 });
const pct = (n) => n == null ? '—' : Number(n).toLocaleString('en-IN', { maximumFractionDigits: 2 }) + '%';

async function api(path, opts = {}) {
  const r = await fetch(path, { headers: { 'Content-Type': 'application/json' }, credentials: 'same-origin', ...opts });
  if (r.status === 401) { location.href = '/login'; throw new Error('unauthenticated'); }
  if (!r.ok) {
    let msg = r.status + ' ' + r.statusText;
    try { const j = await r.json(); if (j.error) msg = j.error; } catch { /* ignore */ }
    throw new Error(msg);
  }
  return r.status === 204 ? null : r.json();
}
const post = (path, body) => api(path, { method: 'POST', body: body === undefined ? '{}' : JSON.stringify(body) });

// ── session ────────────────────────────────────────────────────────────────
let ME = null;
const ROLE_HOME = { SalesAgent: 'Sales', Underwriter: 'Underwriter', SeniorUnderwriter: 'Senior UW', VicePresident: 'VP', Admin: 'Admin' };
const canDecide = () => ['Underwriter', 'SeniorUnderwriter', 'VicePresident', 'Admin'].includes(ME?.role);
const isAdmin = () => ME?.role === 'Admin';

// ── badges ─────────────────────────────────────────────────────────────────
function badge(status) {
  const map = {
    AutoQuoted: 'ox-badge--ok', Approved: 'ox-badge--electric', ApprovedWithChanges: 'ox-badge--electric',
    ReferredToUW: 'ox-badge--warn', ExceptionPending: 'ox-badge--danger', Declined: 'ox-badge--danger',
    Processing: 'ox-badge--vivid', AwaitingDocuments: 'ox-badge--neutral', Draft: 'ox-badge--neutral',
  };
  return `<span class="ox-badge ${map[status] || 'ox-badge--neutral'}">${esc(status)}</span>`;
}
function sevBadge(sev) {
  const map = { Blocking: 'ox-badge--danger', Advisory: 'ox-badge--neutral', HardStop: 'ox-badge--danger', Refer: 'ox-badge--warn', NotApplicable: 'ox-badge--neutral' };
  return `<span class="ox-badge ${map[sev] || 'ox-badge--neutral'}">${esc(sev)}</span>`;
}

// ── shell chrome ───────────────────────────────────────────────────────────
function renderNav() {
  const items = [
    { hash: '#/', label: 'Cases', show: true },
    { hash: '#/exceptions', label: 'Exceptions', show: true },
    { hash: '#/users', label: 'Users', show: isAdmin() },
    { hash: '#/about', label: 'About', show: true },
  ];
  $('#nav').innerHTML = items.filter(i => i.show)
    .map(i => `<a class="ox-nav-item" data-hash="${i.hash}" href="${i.hash}">${i.label}</a>`).join('');
}
function setActiveNav() {
  const h = location.hash || '#/';
  document.querySelectorAll('.ox-nav-item').forEach(a =>
    a.classList.toggle('active', h === a.dataset.hash || (h === '#/' && a.dataset.hash === '#/')));
}
async function refreshBell() {
  try {
    const list = await api('/api/v1/notifications');
    const unread = list.filter(n => !n.isRead).length;
    const b = $('#bell-badge');
    b.textContent = unread; b.classList.toggle('ox-hide', unread === 0);
    const menu = $('#bell-menu');
    menu.innerHTML = list.length === 0
      ? '<div class="ox-bell-empty">No notifications yet.</div>'
      : list.slice(0, 12).map(n => `
        <div class="ox-bell-item ${n.isRead ? 'is-read' : ''}" data-id="${n.notificationId}">
          <div class="ox-bell-title">${esc(n.title)}</div>
          <div class="ox-bell-body">${esc(n.body || '')}</div>
          <div class="ox-bell-meta">${esc(n.caseNumber || '')} · ${new Date(n.createdAtUtc).toLocaleString('en-IN')}</div>
        </div>`).join('') +
      (unread > 0 ? '<button class="ox-btn ox-btn--ghost ox-btn--sm ox-bell-clear" id="bell-clear">Mark all read</button>' : '');
    menu.querySelectorAll('.ox-bell-item').forEach(el =>
      el.addEventListener('click', async () => { await post(`/api/v1/notifications/${el.dataset.id}/read`); refreshBell(); }));
    const clear = $('#bell-clear');
    if (clear) clear.addEventListener('click', async () => { await post('/api/v1/notifications/read-all'); refreshBell(); });
  } catch { /* bell is non-fatal */ }
}

// ── router ─────────────────────────────────────────────────────────────────
const routes = {
  '#/': viewCases,
  '#/exceptions': viewExceptions,
  '#/users': viewUsers,
  '#/about': viewAbout,
  '#/case': viewCase,
};
async function route() {
  const hash = location.hash || '#/';
  const parts = hash.slice(2).split('/');
  if (parts[0] === 'case' && parts[1]) return viewCase(parts[1]);
  const view = routes[hash] || viewCases;
  setActiveNav();
  try { await view(); } catch (e) { $('#view').innerHTML = `<div class="ox-banner ox-banner--error">Failed to load: ${esc(e.message)}</div>`; }
}
window.addEventListener('hashchange', route);

// ── view: cases (dashboard) ────────────────────────────────────────────────
async function viewCases() {
  $('#pagetitle').textContent = 'Cases';
  $('#view').innerHTML = '<div class="ox-skeleton" style="height:120px"></div>';
  const cases = await api('/api/v1/cases');
  const count = (s) => cases.filter(c => c.status === s).length;
  const inProgress = cases.filter(c => ['Draft', 'AwaitingDocuments', 'Processing', 'ExceptionPending'].includes(c.status)).length;

  $('#view').innerHTML = `
    <div class="ox-kpi-row">
      <div class="ox-kpi ox-kpi--primary"><div class="ox-mono-label">TOTAL CASES</div><div class="ox-kpi-num">${cases.length}</div></div>
      <div class="ox-kpi ox-kpi--ok"><div class="ox-mono-label">AUTO-QUOTED</div><div class="ox-kpi-num">${count('AutoQuoted')}</div></div>
      <div class="ox-kpi ox-kpi--warn"><div class="ox-mono-label">REFERRED TO UW</div><div class="ox-kpi-num">${count('ReferredToUW')}</div></div>
      <div class="ox-kpi ox-kpi--electric"><div class="ox-mono-label">APPROVED</div><div class="ox-kpi-num">${count('Approved') + count('ApprovedWithChanges')}</div></div>
      <div class="ox-kpi"><div class="ox-mono-label">IN PROGRESS</div><div class="ox-kpi-num">${inProgress}</div></div>
    </div>
    ${canDecide() ? '' : ''}
    <div class="ox-row ox-row--between ox-row--wrap" style="margin-bottom:12px">
      <div class="ox-mono-label">ALL CASES</div>
      <button class="ox-btn ox-btn--primary" id="new-case">+ New case</button>
    </div>
    <div id="case-list"></div>
    <div class="ox-drawer-backdrop ox-hide" id="drawer-backdrop"></div>
    <aside class="ox-drawer ox-hide" id="drawer"></aside>`;
  renderCaseTable(cases);
  $('#new-case').addEventListener('click', openNewCaseDrawer);
}

function renderCaseTable(cases) {
  const el = $('#case-list');
  if (cases.length === 0) {
    el.innerHTML = `<div class="ox-empty-state"><p class="ox-mb-0">No cases yet. Create your first case — the 13-stage pipeline quotes it automatically once documents land.</p></div>`;
    return;
  }
  el.innerHTML = `
    <div class="ox-table-wrap"><table class="ox-table ox-case-table">
      <thead><tr><th>Case</th><th>Client</th><th>Type</th><th>Stage</th><th>Status</th><th></th></tr></thead>
      <tbody>${cases.map(c => `
        <tr>
          <td class="ox-mono" data-th="Case">${esc(c.caseNumber)}</td>
          <td data-th="Client">${esc(c.clientName)}</td>
          <td data-th="Type">${esc(c.businessType)} · ${esc(c.businessSubType)}</td>
          <td data-th="Stage">${esc(c.currentStage || '—')}</td>
          <td data-th="Status">${badge(c.status)}</td>
          <td class="ox-right"><a class="ox-btn ox-btn--ghost ox-btn--sm" href="#/case/${c.caseId}">Open →</a></td>
        </tr>`).join('')}</tbody>
    </table></div>`;
}

// ── new case drawer + upload + live pipeline ───────────────────────────────
function openNewCaseDrawer() {
  const backdrop = $('#drawer-backdrop'), drawer = $('#drawer');
  drawer.innerHTML = `
    <div class="ox-mono-label">NEW CASE</div>
    <form id="nc-form">
      <label class="ox-mono-label" for="nc-client">CLIENT NAME</label>
      <input class="ox-input" id="nc-client" required placeholder="e.g. ABC Tech Pvt Ltd">
      <label class="ox-mono-label" for="nc-type">BUSINESS TYPE</label>
      <select class="ox-select" id="nc-type">
        <option value="Rollover">Rollover (claims experience available)</option>
        <option value="Renewal-External">Renewal — external insurer</option>
        <option value="Renewal-Internal">Renewal — with us</option>
        <option value="Fresh">Fresh (no experience)</option>
      </select>
      <label class="ox-mono-label" for="nc-sub">SUB TYPE</label>
      <select class="ox-select" id="nc-sub"><option>Main</option><option>TopUp</option></select>
      <label class="ox-mono-label" for="nc-tpa">TPA</label>
      <input class="ox-input" id="nc-tpa" placeholder="e.g. HealthServe">
      <div class="ox-hr"></div>
      <div class="ox-mono-label">DOCUMENTS (xlsx / csv / pdf)</div>
      <p class="ox-muted" style="font-size:12px">Rollover requires: Claims Register · Enrollment · Expiring Policy · RFQ. Fresh requires Enrollment + RFQ.</p>
      ${['ClaimsRegister', 'Enrollment', 'ExpiringPolicy', 'Rfq'].map(dt => `
        <div class="ox-field">
          <span class="ox-mono-label">${dt.replace(/([a-z])([A-Z])/g, '$1 $2').toUpperCase()}</span>
          <input class="ox-input" type="file" id="nc-file-${dt}" accept=".xlsx,.csv,.pdf,.txt">
        </div>`).join('')}
      <div class="ox-banner ox-banner--error ox-hide" id="nc-error"></div>
      <button class="ox-btn ox-btn--primary ox-btn--lg" type="submit" id="nc-submit">Create & run pipeline</button>
    </form>`;
  backdrop.classList.remove('ox-hide'); drawer.classList.remove('ox-hide');
  backdrop.onclick = closeDrawer;
  $('#nc-form').addEventListener('submit', submitNewCase);
}
function closeDrawer() {
  $('#drawer-backdrop').classList.add('ox-hide');
  $('#drawer').classList.add('ox-hide');
}

async function submitNewCase(e) {
  e.preventDefault();
  const err = $('#nc-error'); err.classList.add('ox-hide');
  const btn = $('#nc-submit'); btn.disabled = true; btn.textContent = 'Working…';
  try {
    const businessType = $('#nc-type').value;
    const c = await post('/api/v1/cases', { clientName: $('#nc-client').value, businessType, businessSubType: $('#nc-sub').value, tpa: $('#nc-tpa').value });
    // upload whatever files were picked (real parsing); missing optional docs are skipped per business-type rules
    for (const dt of ['ClaimsRegister', 'Enrollment', 'ExpiringPolicy', 'Rfq']) {
      const inp = $(`#nc-file-${dt}`);
      if (inp?.files?.[0]) {
        const fd = new FormData();
        fd.append('file', inp.files[0]); fd.append('docType', dt);
        const r = await fetch(`/api/v1/cases/${c.caseId}/documents`, { method: 'POST', body: fd, credentials: 'same-origin' });
        if (!r.ok) throw new Error(`${dt} upload failed: ${r.status}`);
      }
    }
    closeDrawer();
    location.hash = `#/case/${c.caseId}?run=1`;
  } catch (ex) {
    err.textContent = ex.message; err.classList.remove('ox-hide');
    btn.disabled = false; btn.textContent = 'Create & run pipeline';
  }
}

// ── view: case detail ──────────────────────────────────────────────────────
async function viewCase(id) {
  $('#pagetitle').textContent = 'Case ' + id;
  $('#view').innerHTML = '<div class="ox-skeleton" style="height:220px"></div>';
  const c = await api(`/api/v1/cases/${id}`);
  const [docs, stages, quotes] = await Promise.all([
    api(`/api/v1/cases/${id}/documents`).catch(() => []),
    api(`/api/v1/cases/${id}/pipeline`).catch(() => []),
    api(`/api/v1/cases/${id}/quotes`).catch(() => []),
  ]);
  const quote = quotes.filter(q => q.status === 'Current').at(-1) || quotes.at(-1);
  const runParam = new URLSearchParams(location.hash.split('?')[1] || '');
  const shouldRun = runParam.get('run') === '1' && (c.status === 'Draft' || c.status === 'AwaitingDocuments');

  $('#view').innerHTML = `
    <a class="ox-muted" href="#/" style="font-size:13px">← All cases</a>
    <div class="ox-card" style="margin-top:8px">
      <div class="ox-row ox-row--between ox-row--wrap">
        <div>
          <div class="ox-mono">${esc(c.caseNumber)}</div>
          <div class="ox-pagetitle" style="margin-top:2px">${esc(c.clientName)}</div>
          <div class="ox-muted" style="font-size:13px">${esc(c.businessType)} · ${esc(c.businessSubType)} · created ${new Date(c.createdAtUtc).toLocaleString('en-IN')}</div>
        </div>
        <div class="ox-right">${badge(c.status)}</div>
      </div>
    </div>
    <div class="ox-grid-2" style="margin-top:16px">
      <div class="ox-card">
        <div class="ox-mono-label">PIPELINE — 13 STAGES</div>
        <div id="stages">${stageTimeline(stages)}</div>
        ${c.status === 'AwaitingDocuments' ? '<div class="ox-banner ox-banner--info">Awaiting documents — pipeline starts automatically when required docs are parsed.</div>' : ''}
        ${c.status === 'Draft' ? '<button class="ox-btn ox-btn--primary" id="run-pipeline" style="margin-top:10px">Run pipeline</button>' : ''}
      </div>
      <div class="ox-card">
        <div class="ox-mono-label">DOCUMENTS (${docs.length})</div>
        ${docs.length === 0 ? '<p class="ox-muted">No documents uploaded.</p>' : `
          <div class="ox-table-wrap"><table class="ox-table"><thead><tr><th>Type</th><th>File</th><th>Parse</th></tr></thead>
          <tbody>${docs.map(d => `<tr><td data-th="Type">${esc(d.docType)}</td><td data-th="File">${esc(d.fileName)}</td><td data-th="Parse"><span class="ox-badge ${d.parseStatus === 'Parsed' ? 'ox-badge--ok' : 'ox-badge--neutral'}">${esc(d.parseStatus)}</span></td></tr>`).join('')}</tbody></table></div>`}
      </div>
    </div>
    <div id="quote-section">${quote ? quoteSection(quote) : (c.status === 'AutoQuoted' ? '<div class="ox-skeleton" style="height:120px;margin-top:16px"></div>' : '')}</div>
    <div id="decide-section"></div>`;

  const runBtn = $('#run-pipeline');
  if (runBtn) runBtn.addEventListener('click', () => startPipeline(id));
  if (shouldRun || runBtn) startPipeline(id, { quiet: !runBtn });
  if (quote) renderDecisions(id, quote);
}

function stageTimeline(stages) {
  if (!stages || stages.length === 0) return '<p class="ox-muted">Pipeline has not run yet.</p>';
  const ORDER = ['Intake', 'Profile', 'ExtractDocuments', 'Map', 'Standardize', 'CodeClinical', 'Analyze', 'Guardrail', 'Price', 'CostBenefits', 'CompareCompetition', 'AssembleQuote', 'Publish'];
  const byName = Object.fromEntries(stages.map(s => [s.stage, s]));
  return `<div class="ox-stagelist">${ORDER.map(name => {
    const s = byName[name];
    if (!s) return `<div class="ox-stagerow is-pending"><span class="ox-stage-dot"></span><span class="ox-stagename">${name}</span><span class="ox-right ox-muted">pending</span></div>`;
    const cls = s.state === 'Completed' ? 'is-done' : s.state === 'Failed' ? 'is-failed' : s.state === 'Skipped' ? 'is-skip' : '';
    const dot = s.state === 'Completed' ? 'ox-stage-dot--completed' : s.state === 'Failed' ? 'ox-stage-dot--failed' : '';
    return `<div class="ox-stagerow ${cls}">
      <span class="ox-stage-dot ${dot}"></span>
      <span class="ox-stagename">${name}</span>
      <span class="ox-right"><span class="ox-badge ${s.state === 'Completed' ? 'ox-badge--ok' : s.state === 'Failed' ? 'ox-badge--danger' : 'ox-badge--neutral'}">${esc(s.state)}</span>
      <span class="ox-muted ox-num" style="margin-left:8px">${s.elapsedSeconds != null ? Number(s.elapsedSeconds).toFixed(2) + 's' : ''}</span></span>
    </div>`;
  }).join('')}</div>`;
}

async function startPipeline(id, { quiet = false } = {}) {
  try {
    if (!quiet) { const el = $('#stages'); if (el) el.innerHTML = '<div class="ox-progress ox-progress--electric" style="height:3px"></div>'; }
    await post(`/api/v1/cases/${id}/pipeline/start`);
    // refresh from persistence after the run completes
    const [c, stages, quotes] = await Promise.all([
      api(`/api/v1/cases/${id}`), api(`/api/v1/cases/${id}/pipeline`), api(`/api/v1/cases/${id}/quotes`)]);
    const se = $('#stages'); if (se) se.innerHTML = stageTimeline(stages);
    const qs = $('#quote-section');
    const quote = quotes.filter(q => q.status === 'Current').at(-1) || quotes.at(-1);
    if (qs && quote) { qs.innerHTML = quoteSection(quote); renderDecisions(id, quote); }
    refreshBell();
  } catch (ex) {
    const se = $('#stages');
    if (se) se.innerHTML = `<div class="ox-banner ox-banner--error">Pipeline failed: ${esc(ex.message)}</div>`;
  }
}

// ── quote: KPIs + bridge waterfall + guardrails ────────────────────────────
function quoteSection(q) {
  const p = q.pricing || {};
  const comps = (p.components || []).slice().sort((a, b) => a.stepOrder - b.stepOrder);
  const find = (re) => comps.filter(c => re.test(c.componentName));
  const tech = find(/technical premium/i).at(-1);
  const gst = find(/^gst/i).at(-1);
  const total = find(/total premium/i).at(-1);
  const gross = find(/grossed-up/i).at(-1);

  return `
  <div class="ox-card" style="margin-top:16px">
    <div class="ox-row ox-row--between ox-row--wrap">
      <div class="ox-mono-label">QUOTE V${q.versionNo} · ${esc(q.status.toUpperCase())}</div>
      <div class="ox-right ox-muted" style="font-size:12px">${q.generationMode} · master ${esc(p.masterVersionRef || '—')}</div>
    </div>
    <div class="ox-kpi-row" style="margin-top:10px">
      <div class="ox-kpi ox-kpi--primary"><div class="ox-mono-label">TECHNICAL PREMIUM</div><div class="ox-kpi-num">${inr(tech?.value ?? p.technicalPremium)}</div></div>
      <div class="ox-kpi"><div class="ox-mono-label">GST 18%</div><div class="ox-kpi-num">${inr(gst?.value ?? 0)}</div></div>
      <div class="ox-kpi ox-kpi--ok"><div class="ox-mono-label">TOTAL INCL. GST</div><div class="ox-kpi-num">${inr(total?.value ?? 0)}</div></div>
      <div class="ox-kpi"><div class="ox-mono-label">GROSS-UP</div><div class="ox-kpi-num">${pct(gross?.value != null && tech?.value ? (gross.value / tech.value - 1) * 100 : null)}</div></div>
    </div>
    <div class="ox-hr"></div>
    <div class="ox-mono-label">PRICING BRIDGE — OBSERVED BURN → TOTAL PREMIUM</div>
    <div class="ox-bridge">${comps.map((c, i) => `
      <div class="ox-bridge-row ${c.isOverride ? 'is-override' : ''}">
        <span class="ox-bridge-step ox-mono">${String(c.stepOrder).padStart(2, '0')}</span>
        <span class="ox-bridge-name">${esc(c.componentName)}${c.isOverride ? ' <span class="ox-badge ox-badge--warn">override</span>' : ''}</span>
        <span class="ox-bridge-val ox-num">${esc(fmtComp(c))}</span>
        <span class="ox-bridge-basis ox-muted">${esc(c.basis || '')}</span>
      </div>`).join('')}</div>
  </div>
  <div class="ox-grid-2" style="margin-top:16px">
    <div class="ox-card">
      <div class="ox-mono-label">GUARDRAILS</div>
      <div id="guardrails">${q.guardrails ? guardrailPanel(q.guardrails) : '<p class="ox-muted">Not evaluated.</p>'}</div>
    </div>
    <div class="ox-card">
      <div class="ox-mono-label">ANALYTICS SNAPSHOT</div>
      ${q.analytics ? analyticsPanel(q.analytics) : '<p class="ox-muted">No snapshot.</p>'}
    </div>
  </div>`;
}

function fmtComp(c) {
  if ((c.unit || '').includes('life')) return inr(c.value) + ' /life';
  if (c.unit === '%') return pct(c.value);
  if ((c.unit || '').includes('₹')) return inr(c.value);
  return String(c.value);
}

function guardrailPanel(g) {
  const rows = (g.evaluations || []).map(e => `
    <div class="ox-grrow">
      <span>${e.passed ? '✓' : '✗'} <span class="ox-mono">${esc(e.ruleCode)}</span></span>
      <span class="ox-right">${esc(e.observed)} ${esc(e.operator)} ${esc(e.threshold)} ${sevBadge(e.severity)}</span>
    </div>`).join('');
  const outcome = g.outcome === 'AutoQuote'
    ? '<div class="ox-banner ox-banner--success">AutoQuote — within appetite. Confidence ' + Number(g.confidence).toFixed(1) + '</div>'
    : '<div class="ox-banner ox-banner--warn">' + esc(g.outcome) + ' — confidence ' + Number(g.confidence).toFixed(1) + '</div>';
  return outcome + rows;
}

function analyticsPanel(a) {
  const items = [
    ['Covered lives', a.coveredLives], ['Employees', a.employeeCount], ['Claims', a.claimCount],
    ['Incurred ₹', inr(a.amountIncurred)], ['Burn / life', inr(a.burnCostPerLife)], ['Avg age', a.avgAge],
    ['Large-claim share', pct(a.largeClaimSharePct)], ['Incurred ratio', pct(a.incurredClaimsRatioPct)],
  ];
  return `<div class="ox-ana">${items.map(([k, v]) => `<div class="ox-ana-row"><span class="ox-muted">${k}</span><span class="ox-num">${esc(v ?? '—')}</span></div>`).join('')}</div>`;
}

// ── decisions (role-gated) ─────────────────────────────────────────────────
function renderDecisions(caseId, quote) {
  const el = $('#decide-section');
  if (!el) return;
  if (!canDecide()) { el.innerHTML = ''; return; }
  el.innerHTML = `
    <div class="ox-card" style="margin-top:16px">
      <div class="ox-mono-label">UNDERWRITING DECISION</div>
      <div class="ox-row ox-row--wrap" style="gap:8px;margin-top:10px">
        <input class="ox-input" id="dev-input" type="number" step="0.1" min="0" max="100" placeholder="Deviation % e.g. 7.9" style="max-width:220px">
        <button class="ox-btn ox-btn--ghost" id="dev-check">Check authority</button>
        <span id="dev-result" class="ox-mono"></span>
      </div>
      <div class="ox-hr"></div>
      <textarea class="ox-textarea" id="dec-remarks" placeholder="Remarks (recorded permanently with the decision)" rows="2"></textarea>
      <div class="ox-row ox-row--wrap" style="gap:8px;margin-top:8px">
        <button class="ox-btn ox-btn--primary" data-act="Approve">Approve</button>
        <button class="ox-btn ox-btn--ghost" data-act="ApproveWithChanges">Approve w/ changes</button>
        <button class="ox-btn ox-btn--danger-ghost" data-act="Reject">Reject</button>
        <button class="ox-btn ox-btn--ghost" data-act="Escalate">Escalate</button>
      </div>
      <div class="ox-banner ox-banner--error ox-hide" id="dec-error" style="margin-top:8px"></div>
    </div>`;
  $('#dev-check').addEventListener('click', async () => {
    const dev = parseFloat($('#dev-input').value);
    if (isNaN(dev)) return;
    const res = await post('/api/v1/uw/authority/check', { deviationPct: dev });
    $('#dev-result').textContent = `${res.decision} → ${res.requiredRole}`;
  });
  el.querySelectorAll('[data-act]').forEach(b => b.addEventListener('click', async () => {
    const err = $('#dec-error'); err.classList.add('ox-hide');
    try {
      const res = await post('/api/v1/uw/decisions', { caseId, action: b.dataset.act, remarks: $('#dec-remarks').value });
      el.innerHTML = `<div class="ox-banner ox-banner--success">${esc(res.action)} recorded — case is now <b>${esc(res.caseStatus)}</b>.</div>`;
    } catch (ex) { err.textContent = ex.message; err.classList.remove('ox-hide'); }
  }));
}

// ── view: exceptions ───────────────────────────────────────────────────────
async function viewExceptions() {
  $('#pagetitle').textContent = 'Exceptions';
  $('#view').innerHTML = '<div class="ox-skeleton" style="height:120px"></div>';
  const exc = await api('/api/v1/exceptions');
  const blocking = exc.filter(e => (e.severity || (e.blocking ? 'Blocking' : 'Advisory')) === 'Blocking');
  const advisory = exc.filter(e => !blocking.includes(e));
  $('#view').innerHTML = `
    <div class="ox-kpi-row">
      <div class="ox-kpi ox-kpi--danger"><div class="ox-mono-label">BLOCKING</div><div class="ox-kpi-num">${blocking.length}</div></div>
      <div class="ox-kpi"><div class="ox-mono-label">ADVISORY</div><div class="ox-kpi-num">${advisory.length}</div></div>
    </div>
    ${exc.length === 0 ? '<div class="ox-empty-state"><p class="ox-mb-0">No open exceptions. The pipeline is clean.</p></div>' : `
    <div class="ox-table-wrap"><table class="ox-table">
      <thead><tr><th>Case</th><th>Code</th><th>Severity</th><th>Message</th><th>Fix at</th><th></th></tr></thead>
      <tbody>${[...blocking, ...advisory].map(e => `
        <tr>
          <td class="ox-mono" data-th="Case">${esc(e.caseNumber || e.caseId)}</td>
          <td class="ox-mono" data-th="Code">${esc(e.code)}</td>
          <td data-th="Severity">${sevBadge(e.severity || (e.blocking ? 'Blocking' : 'Advisory'))}</td>
          <td data-th="Message">${esc(e.message)}</td>
          <td data-th="Fix at">${esc(e.resolutionTarget || '—')}</td>
          <td class="ox-right">${e.caseId ? `<a class="ox-btn ox-btn--ghost ox-btn--sm" href="#/case/${e.caseId}">Open →</a>` : ''}</td>
        </tr>`).join('')}</tbody>
    </table></div>`}`;
}

// ── view: users (admin) ────────────────────────────────────────────────────
async function viewUsers() {
  if (!isAdmin()) { $('#view').innerHTML = '<div class="ox-banner ox-banner--error">Admins only.</div>'; return; }
  $('#pagetitle').textContent = 'Users';
  const users = await api('/api/v1/users');
  $('#view').innerHTML = `
    <div class="ox-table-wrap"><table class="ox-table">
      <thead><tr><th>Email</th><th>Name</th><th>Role</th><th>Active</th><th>Last login</th></tr></thead>
      <tbody>${users.map(u => `
        <tr>
          <td class="ox-mono" data-th="Email">${esc(u.email)}</td>
          <td data-th="Name">${esc(u.displayName)}</td>
          <td data-th="Role"><span class="ox-badge ox-badge--vivid">${esc(u.role)}</span></td>
          <td data-th="Active">${u.isActive ? '✓' : '✗'}</td>
          <td data-th="Last login">${u.lastLoginUtc ? new Date(u.lastLoginUtc).toLocaleString('en-IN') : '—'}</td>
        </tr>`).join('')}</tbody>
    </table></div>`;
}

// ── view: about ────────────────────────────────────────────────────────────
function viewAbout() {
  $('#pagetitle').textContent = 'About';
  $('#view').innerHTML = `
    <div class="ox-card">
      <div class="ox-mono-label">GMC v2.0 — AI-FIRST GROUP MEDICLAIM UNDERWRITING</div>
      <p>13-stage autonomous pipeline: Intake → Profile → Extract → Map → Standardize → CodeClinical → Analyze → Guardrail → Price → CostBenefits → CompareCompetition → AssembleQuote → Publish.</p>
      <p>Money is computed <b>deterministically</b> from an effective-dated master + persisted input snapshot — the AI tier enriches, never decides amounts. Every value carries confidence + provenance. Authority is enforced server-side.</p>
      <div class="ox-hr"></div>
      <div class="ox-mono-label">AI TIER</div>
      <p class="ox-muted">Gateway abstraction live (Extraction · Mapping · Clinical · Narrative agents). Provider wiring pending — deterministic tier fully functional without it.</p>
    </div>`;
}

// ── boot ───────────────────────────────────────────────────────────────────
async function boot() {
  try { ME = await api('/api/v1/auth/me'); }
  catch { return; }
  $('#who-name').textContent = ME.displayName || ME.role;
  $('#who-role').textContent = ROLE_HOME[ME.role] || ME.role;
  renderNav();
  // burger (mobile)
  $('#burger').addEventListener('click', () => $('#sidebar').classList.toggle('ox-sidebar-open'));
  document.addEventListener('click', (e) => {
    if (!e.target.closest('.ox-bell-wrap')) $('#bell-menu')?.classList.add('ox-hide');
    else $('#bell-menu')?.classList.toggle('ox-hide');
  });
  $('#logout').addEventListener('click', async () => { await post('/api/v1/auth/logout'); location.href = '/login'; });
  route();
  refreshBell();
}
boot();
})();
