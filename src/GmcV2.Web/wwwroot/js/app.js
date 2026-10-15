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
    let body = null;
    try { const j = await r.json(); if (j.error) msg = j.error; body = j; } catch { /* ignore */ }
    const err = new Error(msg);
    if (body) err.conflict = body.conflict ?? null;   // 409 retarget conflicts surface in the drawer
    throw err;
  }
  return r.status === 204 ? null : r.json();
}
const post = (path, body) => api(path, { method: 'POST', body: body === undefined ? '{}' : JSON.stringify(body) });

// ── session ────────────────────────────────────────────────────────────────
let ME = null;
const ROLE_HOME = { SalesAgent: 'Sales', Underwriter: 'Underwriter', SeniorUnderwriter: 'Senior UW', VicePresident: 'VP', Admin: 'Admin' };
const canDecide = () => ['Underwriter', 'SeniorUnderwriter', 'VicePresident', 'Admin'].includes(ME?.role);
const isAdmin = () => ME?.role === 'Admin';
// views-analytics.js is a separate script — expose the session helpers globally (const in an
// IIFE is script-scoped, invisible to other scripts; this bit us in production).
window.canDecide = canDecide; window.isAdmin = isAdmin; window.getME = () => ME;
// shared view helpers used by views-*.js (separate scripts; consts here are IIFE-scoped)
window.$ = $; window.esc = esc; window.inr = inr; window.api = api;

// ── badges ─────────────────────────────────────────────────────────────────
function badge(status) {
  return `<span class="ox-badge ${badgeClass(status)}">${esc(status)}</span>`;
}
function badgeClass(status) {
  const map = {
    AutoQuoted: 'ox-badge--ok', Approved: 'ox-badge--electric', ApprovedWithChanges: 'ox-badge--electric',
    ReferredToUW: 'ox-badge--warn', ExceptionPending: 'ox-badge--danger', Declined: 'ox-badge--danger',
    Processing: 'ox-badge--vivid', AwaitingDocuments: 'ox-badge--neutral', Draft: 'ox-badge--neutral',
  };
  return map[status] || 'ox-badge--neutral';
}
function sevBadge(sev) {
  const map = { Blocking: 'ox-badge--danger', Advisory: 'ox-badge--neutral', HardStop: 'ox-badge--danger', Refer: 'ox-badge--warn', NotApplicable: 'ox-badge--neutral' };
  return `<span class="ox-badge ${map[sev] || 'ox-badge--neutral'}">${esc(sev)}</span>`;
}

// ── shell chrome ───────────────────────────────────────────────────────────
function renderNav() {
  const items = [
    { hash: '#/', label: 'Cases', show: true },
    { hash: '#/coding', label: 'Coding', show: canDecide() },
    { hash: '#/mapping', label: 'Mapping', show: canDecide() },
    { hash: '#/analytics', label: 'Analytics', show: canDecide() },
    { hash: '#/benefits', label: 'Benefit Delta', show: canDecide() },
    { hash: '#/quote', label: 'Final Quote', show: canDecide() },
    { hash: '#/standardize', label: 'Standardize', show: canDecide() },
    { hash: '#/exceptions', label: 'Exceptions', show: true },
    { hash: '#/users', label: 'Users', show: isAdmin() },
    { hash: '#/admin', label: 'Admin', show: isAdmin() },
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
  '#/admin': viewAdmin,
  '#/about': viewAbout,
  '#/case': viewCase,
  '#/coding': viewCoding,
  '#/mapping': viewMapping,
  '#/analytics': (...a) => viewAnalytics(...a),
  '#/benefits': viewBenefits,
  '#/quote': viewFinalQuote,
  '#/standardize': viewStandardize,
};
async function route() {
  const hash = location.hash || '#/';
  const parts = hash.slice(2).split('/');
  if (parts[0] === 'case' && parts[1]) {
    try { return await viewCase(parts[1]); }
    catch (e) { $('#view').innerHTML = `<div class="ox-banner ox-banner--error">Failed to load case: ${esc(e.message)}</div>`; return; }
  }
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
  requestAnimationFrame(() => { backdrop.classList.add('open'); drawer.classList.add('open'); });
  backdrop.onclick = closeDrawer;
  $('#nc-form').addEventListener('submit', submitNewCase);
}
function closeDrawer() {
  const b = $('#drawer-backdrop'), d = $('#drawer');
  b.classList.remove('open'); d.classList.remove('open');
  setTimeout(() => { b.classList.add('ox-hide'); d.classList.add('ox-hide'); }, 180);
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
        const r = await fetch(`/api/v1/cases/${c.caseId}/documents/upload`, { method: 'POST', body: fd, credentials: 'same-origin' });
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

// required docs per business type (mirror of Enums.RequiredDocuments)
const REQUIRED_DOCS = {
  'Rollover': ['ClaimsRegister', 'Enrollment', 'ExpiringPolicy', 'Rfq'],
  'Fresh': ['Enrollment', 'Rfq'],
  'Renewal-Internal': ['ClaimsRegister', 'Enrollment'],
  'Renewal-External': ['ClaimsRegister', 'Enrollment', 'ExpiringPolicy', 'Rfq'],
};

// ── view: case detail ──────────────────────────────────────────────────────
async function viewCase(id) {
  id = String(id).split('?')[0];          // '#/case/29?run=1' → '29'
  if (!/^\d+$/.test(id)) { $('#view').innerHTML = '<div class="ox-banner ox-banner--error">Invalid case reference.</div>'; return; }
  $('#pagetitle').textContent = 'Case ' + id;
  $('#view').innerHTML = '<div class="ox-skeleton" style="height:220px"></div>';
  const c = await api(`/api/v1/cases/${id}`);
  const [docs, stages, quotes] = await Promise.all([
    api(`/api/v1/cases/${id}/documents`).catch(() => []),
    api(`/api/v1/cases/${id}/pipeline`).catch(() => []),
    api(`/api/v1/cases/${id}/quotes`).catch(() => []),
  ]);
  const quote = quotes.filter(q => q.status === 'Current').at(-1) || quotes.at(-1);
  const required = REQUIRED_DOCS[c.businessType] || [];
  const have = new Set(docs.map(d => d.docType));
  const missing = required.filter(dt => !have.has(dt));
  const runParam = new URLSearchParams(location.hash.split('?')[1] || '');
  const shouldRun = runParam.get('run') === '1' && (c.status === 'Draft' || c.status === 'AwaitingDocuments');
  // exception CTA: the case object carries open exceptions — first clinical/mapping one wins
  const openExc = (c.exceptions || []).find(e => e.status !== 'Resolved' && (isClinical(e.code) || isMappingExc(e.code)));
  const openClinical = openExc && isClinical(openExc.code) ? openExc.code : null;
  const openMapping = openExc && isMappingExc(openExc.code) ? openExc.code : null;

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
        ${missing.length ? `
          <div class="ox-banner ox-banner--info">
            <strong>Awaiting documents</strong> — pipeline starts when all required docs are parsed.
            <div style="margin-top:8px">
              ${missing.map(dt => `
                <div class="ox-field" style="margin:6px 0">
                  <span class="ox-mono-label" style="color:var(--ox-warn,#EAA40E)">MISSING: ${dt.replace(/([a-z])([A-Z])/g,'$1 $2').toUpperCase()}</span>
                  <input class="ox-input" type="file" id="cd-file-${dt}" accept=".xlsx,.csv,.pdf,.txt">
                </div>`).join('')}
              <button class="ox-btn ox-btn--primary" id="cd-upload" style="margin-top:6px">Upload & run</button>
            </div>
          </div>` : ''}
        ${c.status === 'Draft' && !missing.length ? '<button class="ox-btn ox-btn--primary" id="run-pipeline" style="margin-top:10px">Run pipeline</button>' : ''}
      </div>
      <div class="ox-card">
        <div class="ox-mono-label">DOCUMENTS (${docs.length})</div>
        ${docs.length === 0 ? '<p class="ox-muted">No documents uploaded.</p>' : `
          <div class="ox-table-wrap"><table class="ox-table"><thead><tr><th>Type</th><th>File</th><th>Parse</th></tr></thead>
          <tbody>${docs.map(d => `<tr><td data-th="Type">${esc(d.docType)}</td><td data-th="File">${esc(d.fileName)}</td><td data-th="Parse"><span class="ox-badge ${d.parseStatus === 'Parsed' ? 'ox-badge--ok' : 'ox-badge--neutral'}">${esc(d.parseStatus)}</span></td></tr>`).join('')}</tbody></table></div>`}
      </div>
    </div>
    <div id="quote-section">${quote ? quoteSection(quote) : (c.status === 'AutoQuoted' ? '<div class="ox-skeleton" style="height:120px;margin-top:16px"></div>' : '')}</div>
    <div id="decide-section"></div>
    ${c.status === 'ExceptionPending' && openClinical ? `
      <div class="ox-banner ox-banner--warn" style="margin-top:16px">
        <strong>${openClinical === 'EXC-CLIN-UNMAPPED' ? 'Unmapped claim rows' : 'Coding review band'} block this case</strong> — resolve them in the coding workbench.
      </div>
      <a class="ox-btn ox-btn--primary" style="margin-top:8px" href="#/coding?case=${id}">Open coding workbench →</a>` : ''}
    ${c.status === 'ExceptionPending' && openMapping ? `
      <div class="ox-banner ox-banner--warn" style="margin-top:16px">
        <strong>Column mapping issues block this case (${esc(openMapping)})</strong> — review and retarget columns in the mapping workbench.
      </div>
      <a class="ox-btn ox-btn--primary" style="margin-top:8px" href="#/mapping?case=${id}">Open mapping workbench →</a>` : ''}`;

  const runBtn = $('#run-pipeline');
  if (runBtn) runBtn.addEventListener('click', () => startPipeline(id));

  // case-page upload for missing required docs → upload all, then run pipeline
  const upBtn = $('#cd-upload');
  if (upBtn) upBtn.addEventListener('click', async () => {
    const btn = upBtn; btn.disabled = true; btn.textContent = 'Uploading…';
    try {
      for (const dt of missing) {
        const inp = $(`#cd-file-${dt}`);
        if (inp?.files?.[0]) {
          const fd = new FormData();
          fd.append('file', inp.files[0]); fd.append('docType', dt);
          const r = await fetch(`/api/v1/cases/${id}/documents/upload`, { method: 'POST', body: fd, credentials: 'same-origin' });
          if (!r.ok) throw new Error(`${dt} upload failed: ${r.status}`);
        }
      }
      btn.textContent = 'Running pipeline…';
      await startPipeline(id);
      route();
    } catch (ex) {
      btn.disabled = false; btn.textContent = 'Upload & run';
      alert(ex.message);
    }
  });
  if (shouldRun || runBtn) startPipeline(id, { quiet: !runBtn });
  if (quote) { renderDecisions(id, quote); wireQuoteOverride(id, quote); wireSIScenario(id); wireDiscountLoop(id); }
}

function stageTimeline(stages) {
  if (!stages || stages.length === 0) return '<p class="ox-muted">Pipeline has not run yet.</p>';
  const ORDER = ['Intake', 'Profile', 'ExtractDocuments', 'Map', 'Standardize', 'CodeClinical', 'Analyze', 'Guardrail', 'Price', 'CostBenefits', 'CompareCompetition', 'AssembleQuote', 'Publish'];
  const byName = Object.fromEntries(stages.map(s => [s.stage, s]));
  return `<div class="ox-stagelist">${ORDER.map(name => {
    const s = byName[name];
    if (!s) return `<div class="ox-stagerow is-pending"><span class="ox-stage-dot"></span><span class="ox-stagename">${name}</span><span class="ox-stagedur">pending</span></div>`;
    const stateCls = s.state === 'Completed' ? 'is-completed' : s.state === 'Failed' ? 'is-failed' : s.state === 'Running' ? 'is-running' : s.state === 'Skipped' ? 'is-skip' : '';
    const dot = s.state === 'Completed' ? 'ox-stage-dot--completed' : s.state === 'Failed' ? 'ox-stage-dot--failed' : s.state === 'Running' ? 'ox-stage-dot--running' : '';
    const badgeCls = s.state === 'Completed' ? 'ox-badge--ok' : s.state === 'Failed' ? 'ox-badge--danger' : 'ox-badge--neutral';
    return `<div class="ox-stagerow ${stateCls}">
      <span class="ox-stage-dot ${dot}"></span>
      <span class="ox-stagename">${name}</span>
      <span class="ox-badge ${badgeCls}">${esc(s.state)}</span>
      <span class="ox-stagedur">${s.elapsedSeconds != null ? Number(s.elapsedSeconds).toFixed(2) + 's' : ''}</span>
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
    if (qs && quote) { qs.innerHTML = quoteSection(quote); renderDecisions(id, quote); wireQuoteOverride(id, quote); wireSIScenario(id); wireDiscountLoop(id); }
    // refresh the status badge + case header from the post-run case object
    const badgeEl = $('#view .ox-card .ox-right .ox-badge');
    if (badgeEl && c?.status) { badgeEl.textContent = c.status; badgeEl.className = 'ox-badge ' + badgeClass(c.status); }
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
    <div class="ox-kpi-row ox-quote-kpis" style="margin-top:10px">
      <div class="ox-kpi ox-kpi--primary"><div class="ox-money-label">TECHNICAL PREMIUM</div><div class="ox-money">${inr(tech?.value ?? p.technicalPremium)}</div><div class="ox-kpi-sub">ANNUAL · PRE-TAX</div></div>
      <div class="ox-kpi"><div class="ox-money-label">GST 18%</div><div class="ox-money">${inr(gst?.value ?? 0)}</div><div class="ox-kpi-sub">CGST + SGST</div></div>
      <div class="ox-kpi ox-kpi--ok"><div class="ox-money-label">TOTAL INCL. GST</div><div class="ox-money">${inr(total?.value ?? 0)}</div><div class="ox-kpi-sub is-up">PAYABLE</div></div>
      <div class="ox-kpi"><div class="ox-money-label">GROSS-UP</div><div class="ox-money">${pct(gross?.value != null && tech?.value ? (gross.value / tech.value - 1) * 100 : null)}</div><div class="ox-kpi-sub">TPA LOAD</div></div>
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
    ${canDecide() ? `
    <div class="ox-hr"></div>
    <div class="ox-row ox-row--between ox-row--wrap">
      <div class="ox-mono-label">SUPERSEDE QUOTE — MANUAL OVERRIDE (§11)</div>
      <button class="ox-btn ox-btn--sm" id="qov-toggle">Override…</button>
    </div>
    <div id="qov-form" class="ox-hide" style="margin-top:10px">
      <div class="ox-grid-2" style="gap:10px">
        <label class="ox-field"><span class="ox-mono-label">DISCOUNT %</span><input class="ox-input" id="qov-disc" type="number" min="0" max="50" step="0.5" placeholder="0"></label>
        <label class="ox-field"><span class="ox-mono-label">OR MANUAL TECHNICAL PREMIUM ₹</span><input class="ox-input" id="qov-prem" type="number" min="0" step="1000" placeholder="auto"></label>
      </div>
      <label class="ox-field" style="margin-top:10px"><span class="ox-mono-label">REASON (MANDATORY, PERMANENTLY RECORDED)</span><input class="ox-input" id="qov-reason" placeholder="e.g. low claim ratio, chronic book loading"></label>
      <div class="ox-row" style="margin-top:10px;gap:8px">
        <button class="ox-btn ox-btn--primary ox-btn--sm" id="qov-apply">Create new version</button>
        <button class="ox-btn ox-btn--sm" id="qov-cancel">Cancel</button>
      </div>
    </div>` : ''}
    <div class="ox-hr"></div>
    <div class="ox-row ox-row--between ox-row--wrap">
      <div class="ox-mono-label">COMMERCIAL DISCOUNT — SALES ↔ UW LOOP</div>
      <button class="ox-btn ox-btn--sm" id="disc-toggle">Request discount…</button>
    </div>
    <div id="disc-form" class="ox-hide" style="margin-top:10px">
      <div class="ox-grid-2" style="gap:10px">
        <label class="ox-field"><span class="ox-mono-label">REQUESTED %</span><input class="ox-input" id="disc-pct" type="number" min="0" max="50" step="0.5" placeholder="0"></label>
        <label class="ox-field"><span class="ox-mono-label">JUSTIFICATION</span>
          <select class="ox-input" id="disc-just">
            <option>CommercialDiscount</option><option>LowClaimRatioDiscount</option>
            <option>Retention</option><option>CompetitorMatch</option>
          </select></label>
      </div>
      <label class="ox-field" style="margin-top:10px"><span class="ox-mono-label">REMARKS</span><input class="ox-input" id="disc-remarks" placeholder="e.g. competitor quote 8% lower, renewal at risk"></label>
      <div class="ox-row" style="margin-top:10px;gap:8px">
        <button class="ox-btn ox-btn--primary ox-btn--sm" id="disc-submit">Submit request</button>
        <button class="ox-btn ox-btn--sm" id="disc-cancel">Cancel</button>
      </div>
    </div>
    <div id="disc-list" style="margin-top:12px"></div>
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
  </div>
  ${canDecide() ? `
  <div class="ox-card" style="margin-top:16px">
    <div class="ox-row ox-row--between ox-row--wrap">
      <div class="ox-mono-label">SUM-INSURED SCENARIO — CHRONIC SEVERITY REPLAY</div>
      <button class="ox-btn ox-btn--sm" id="sis-toggle">Scenario…</button>
    </div>
    <div id="sis-form" class="ox-hide" style="margin-top:10px">
      <div id="sis-cards" class="ox-grid-3" style="gap:10px;margin-bottom:10px"><div class="ox-skeleton" style="height:90px"></div></div>
      <div class="ox-grid-3" style="gap:10px">
        <label class="ox-field"><span class="ox-mono-label">3L→4L REALIZATION</span><input class="ox-input" id="sis-f34" type="number" min="0" max="5" step="0.1" value="1"></label>
        <label class="ox-field"><span class="ox-mono-label">3L→5L REALIZATION</span><input class="ox-input" id="sis-f35" type="number" min="0" max="5" step="0.1" value="1"></label>
        <label class="ox-field"><span class="ox-mono-label">4L→5L REALIZATION</span><input class="ox-input" id="sis-f45" type="number" min="0" max="5" step="0.1" value="1"></label>
      </div>
      <label class="ox-row" style="gap:8px;margin-top:10px;align-items:center">
        <input type="checkbox" id="sis-apply"><span class="ox-muted">Apply to pricing (adds incremental /life to the benefit bridge)</span>
      </label>
      <div class="ox-row" style="margin-top:10px;gap:8px">
        <button class="ox-btn ox-btn--primary ox-btn--sm" id="sis-save">Save scenario</button>
      </div>
    </div>
  </div>` : ''}`;
}

function fmtComp(c) {
  if ((c.unit || '').includes('life')) return inr(c.value) + ' /life';
  if (c.unit === '%') return pct(c.value);
  if ((c.unit || '').includes('₹')) return inr(c.value);
  return String(c.value);
}

// §11 quote override: discount % or manual premium → supersede with a new versioned quote
function wireQuoteOverride(caseId, quote) {
  const t = $('#qov-toggle'); if (!t) return;
  t.addEventListener('click', () => $('#qov-form').classList.toggle('ox-hide'));
  $('#qov-cancel').addEventListener('click', () => $('#qov-form').classList.add('ox-hide'));
  $('#qov-apply').addEventListener('click', async () => {
    const body = {
      discountPct: $('#qov-disc').value ? Number($('#qov-disc').value) : null,
      technicalPremium: $('#qov-prem').value ? Number($('#qov-prem').value) : null,
      reason: $('#qov-reason').value.trim(),
    };
    if (!body.reason) { alert('Reason is mandatory for an override.'); return; }
    try {
      const res = await post(`/api/v1/cases/${caseId}/quotes/override`, body);
      alert(`Quote V${res.versionNo} created — ${inr(res.technicalPremium)} technical premium.`);
      route();
    } catch (ex) { alert('Override failed: ' + ex.message); }
  });
}

// Commercial discount loop (prototype v74 step 8): Sales requests → UW chain decides per authority matrix
async function renderDiscounts(caseId) {
  const list = $('#disc-list'); if (!list) return;
  try {
    const rows = await api(`/api/v1/cases/${caseId}/discounts`);
    list.innerHTML = rows.length === 0 ? '' : `
      <div class="ox-mono-label" style="margin-bottom:6px">REQUEST HISTORY</div>` + rows.map(r => `
      <div class="ox-card" style="padding:10px;margin-bottom:8px">
        <div class="ox-row ox-row--between ox-row--wrap" style="gap:8px">
          <div><strong>${r.requestedPct}%</strong> on V-quote · <span class="ox-muted">${esc(r.requestedByEmail)} · ${new Date(r.requestedAtUtc).toLocaleString()}</span></div>
          <div class="ox-badge ${r.status === 'Approved' ? 'ox-badge--ok' : r.status === 'Declined' ? 'ox-badge--error' : 'ox-badge--warn'}">${esc(r.status)}</div>
        </div>
        ${r.approvedPct != null ? `<div class="ox-muted" style="margin-top:4px">Approved at ${r.approvedPct}% by ${esc(r.approvedByEmail || '')}</div>` : ''}
        ${r.approvalRemarks ? `<div class="ox-muted" style="margin-top:2px">“${esc(r.approvalRemarks)}”</div>` : ''}
        ${r.status === 'Requested' && canDecide() ? `
        <div class="ox-row" style="margin-top:8px;gap:8px">
          <input class="ox-input" style="max-width:110px" id="disc-appr-${r.discountRequestId}" type="number" min="0" max="${r.requestedPct}" step="0.5" value="${r.requestedPct}">
          <button class="ox-btn ox-btn--primary ox-btn--sm" data-approve="${r.discountRequestId}">Approve</button>
          <button class="ox-btn ox-btn--sm" data-decline="${r.discountRequestId}">Decline</button>
        </div>` : ''}
      </div>`).join('');
    list.querySelectorAll('[data-approve]').forEach(b => b.addEventListener('click', async () => {
      const did = b.dataset.approve;
      const pct = Number($(`#disc-appr-${did}`).value || 0);
      try {
        const res = await post(`/api/v1/discounts/${did}/decide`, { approvedPct: pct, remarks: `approved via workbench at ${pct}%` });
        if (res.error) throw new Error(res.error);
        renderDiscounts(caseId);
      } catch (ex) { alert('Decision failed: ' + ex.message); }
    }));
    list.querySelectorAll('[data-decline]').forEach(b => b.addEventListener('click', async () => {
      const did = b.dataset.decline;
      try {
        const res = await post(`/api/v1/discounts/${did}/decide`, { approvedPct: 0, remarks: 'declined via workbench' });
        if (res.error) throw new Error(res.error);
        renderDiscounts(caseId);
      } catch (ex) { alert('Decision failed: ' + ex.message); }
    }));
  } catch { list.innerHTML = ''; }
}

function wireDiscountLoop(caseId) {
  const t = $('#disc-toggle'); if (!t) return;
  t.addEventListener('click', () => $('#disc-form').classList.toggle('ox-hide'));
  $('#disc-cancel').addEventListener('click', () => $('#disc-form').classList.add('ox-hide'));
  $('#disc-submit').addEventListener('click', async () => {
    const pct = Number($('#disc-pct').value || 0);
    if (pct <= 0) { alert('Enter a requested % (0–50).'); return; }
    try {
      const res = await post(`/api/v1/cases/${caseId}/discounts`, {
        requestedPct: pct, justificationType: $('#disc-just').value, remarks: $('#disc-remarks').value.trim() || null,
      });
      if (res.error) throw new Error(res.error);
      $('#disc-form').classList.add('ox-hide');
      $('#disc-pct').value = ''; $('#disc-remarks').value = '';
      renderDiscounts(caseId);
    } catch (ex) { alert('Request failed: ' + ex.message); }
  });
  renderDiscounts(caseId);
}

// SI-scenario (prototype v74 step 5): chronic severity replay across SI bands
function wireSIScenario(caseId) {
  const t = $('#sis-toggle'); if (!t) return;
  t.addEventListener('click', async () => {
    const form = $('#sis-form');
    const opening = form.classList.contains('ox-hide');
    form.classList.toggle('ox-hide');
    if (!opening || $('#sis-cards').dataset.loaded) return;
    try {
      const d = await api(`/api/v1/cases/${caseId}/si-scenario`);
      if (!d.computed) { $('#sis-cards').innerHTML = `<div class="ox-banner ox-banner--warn">${esc(d.message || 'Run the pipeline first.')}</div>`; $('#sis-cards').dataset.loaded = '1'; return; }
      $('#sis-f34').value = d.factors.f34; $('#sis-f35').value = d.factors.f35; $('#sis-f45').value = d.factors.f45;
      $('#sis-apply').checked = !!d.applyToPricing;
      $('#sis-cards').innerHTML = (d.bands || []).map(b => `
        <div class="ox-kpi ${b.incrementalCost > 0 ? 'ox-kpi--warn' : ''}">
          <div class="ox-mono-label">${esc(b.label)}</div>
          <div class="ox-money">${inr(b.incrementalCost)}</div>
          <div class="ox-kpi-sub">historical ${inr(b.historicalCost)} · ${b.claimCount} chronic claims · ${(b.realizationFactor * 100).toFixed(0)}%</div>
        </div>`).join('');
      $('#sis-cards').dataset.loaded = '1';
    } catch (ex) { $('#sis-cards').innerHTML = `<div class="ox-banner ox-banner--error">${esc(ex.message)}</div>`; }
  });
  $('#sis-save').addEventListener('click', async () => {
    try {
      const res = await post(`/api/v1/cases/${caseId}/si-scenario`, {
        factor3To4: Number($('#sis-f34').value || 1),
        factor3To5: Number($('#sis-f35').value || 1),
        factor4To5: Number($('#sis-f45').value || 1),
        applyToPricing: $('#sis-apply').checked,
      });
      alert(res.note || 'Scenario saved.');
      if (res.applyToPricing) route();
    } catch (ex) { alert('Save failed: ' + ex.message); }
  });
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

// ── view: coding workbench (clinical review queue, workflow Stage 6) ───────
let CODING_CASES = []; // caseId → label map for the workbench selector
async function viewCoding() {
  if (!canDecide()) { $('#view').innerHTML = '<div class="ox-banner ox-banner--error">Underwriters only.</div>'; return; }
  $('#pagetitle').textContent = 'Coding Workbench';
  $('#view').innerHTML = '<div class="ox-skeleton" style="height:200px"></div>';
  const cases = await api('/api/v1/cases');
  CODING_CASES = cases;
  // ?case=N deep-link (exception CTAs) → auto-select; otherwise the case with open review work
  const wantCase = new URLSearchParams(location.hash.split('?')[1] || '').get('case');
  const candidates = cases.filter(c => ['ExceptionPending', 'ReferredToUW', 'Processing', 'AutoQuoted', 'Approved', 'ApprovedWithChanges', 'UnderReview'].includes(c.status));
  const sel = wantCase ?? (candidates[0]?.caseId ?? cases[0]?.caseId);
  if (!sel) { $('#view').innerHTML = '<div class="ox-empty-state"><p class="ox-mb-0">No cases yet — run a pipeline first.</p></div>'; return; }
  $('#view').innerHTML = `
    <div class="ox-row ox-row--between ox-row--wrap" style="margin-bottom:12px;gap:8px">
      <select class="ox-select" id="cw-case" style="max-width:420px">${cases.map(c =>
        `<option value="${c.caseId}" ${String(c.caseId) === String(sel) ? 'selected' : ''}>${esc(c.caseNumber)} — ${esc(c.clientName)} (${esc(c.status)})</option>`).join('')}</select>
      <div class="ox-seg" id="cw-filters">
        ${['All', 'Unmapped', 'InReview', 'HumanAccepted'].map((f, i) =>
          `<button class="ox-seg-btn${i === 0 ? ' is-active' : ''}" data-f="${f}">${f === 'InReview' ? 'Review band' : f === 'HumanAccepted' ? 'Human-settled' : f}</button>`).join('')}
      </div>
    </div>
    <div id="cw-panel"></div>
    <div class="ox-drawer-backdrop ox-hide" id="drawer-backdrop"></div>
    <aside class="ox-drawer ox-hide" id="drawer"></aside>`;
  $('#cw-case').addEventListener('change', () => location.hash = `#/coding?case=${$('#cw-case').value}`);
  $('#cw-filters').querySelectorAll('[data-f]').forEach(b => b.addEventListener('click', () => {
    $('#cw-filters').querySelectorAll('[data-f]').forEach(x => x.classList.toggle('is-active', x === b));
    renderCodingPanel(sel, b.dataset.f);
  }));
  await renderCodingPanel(sel, 'All');
}

let CODING_ROWS = [];
async function renderCodingPanel(caseId, filter) {
  const panel = $('#cw-panel');
  panel.innerHTML = '<div class="ox-skeleton" style="height:120px"></div>';
  const data = await api(`/api/v1/cases/${caseId}/coding`);
  CODING_ROWS = data.rows;
  const kpis = [
    ['TOTAL CODED', data.counts.total, ''],
    ['UNMAPPED', data.counts.unmapped, data.counts.unmapped > 0 ? 'ox-kpi--danger' : 'ox-kpi--ok'],
    ['REVIEW BAND', data.counts.inReview, data.counts.inReview > 0 ? 'ox-kpi--warn' : ''],
    ['HUMAN-SETTLED', data.counts.humanSettled, 'ox-kpi--electric'],
  ];
  const rows = CODING_ROWS.filter(r => filter === 'All' ? true
    : filter === 'HumanAccepted' ? (r.codingStatus === 'HumanAccepted' || r.codingStatus === 'HumanCorrected')
    : r.codingStatus === filter);
  const actionable = (r) => r.codingStatus === 'Unmapped' || r.codingStatus === 'InReview';
  const settled = (r) => r.codingStatus === 'HumanAccepted' || r.codingStatus === 'HumanCorrected';
  panel.innerHTML = `
    <div class="ox-kpi-row" style="margin-bottom:14px">
      ${kpis.map(([label, val, cls]) => `<div class="ox-kpi ${cls}"><div class="ox-mono-label">${label}</div><div class="ox-kpi-num">${val}</div></div>`).join('')}
    </div>
    ${data.exceptions && (data.exceptions.unmapped || data.exceptions.review) ? `
      <div class="ox-banner ox-banner--warn" style="margin-bottom:12px">
        <strong>Clinical exceptions open</strong> — ${data.exceptions.unmapped ? `${esc(String(data.exceptions.unmapped))} unmapped rows block the pipeline` : ''}
        ${data.exceptions.unmapped && data.exceptions.review ? ' · ' : ''}${data.exceptions.review ? `${esc(String(data.exceptions.review))} in the review band` : ''}.
        Resolve every row below to release the case.
      </div>` : ''}
    ${rows.length === 0 ? '<div class="ox-empty-state"><p class="ox-mb-0">Nothing in this bucket. The queue is clean.</p></div>' : `
    <div class="ox-table-wrap"><table class="ox-table ox-case-table">
      <thead><tr><th>Claim</th><th>Member</th><th>Diagnosis</th><th>ICD L1</th><th>Conf.</th><th>Provenance</th><th>Incurred</th><th>Status</th><th></th></tr></thead>
      <tbody>${rows.map(r => `
        <tr>
          <td class="ox-mono" data-th="Claim">${esc(r.claimNumber)}</td>
          <td class="ox-mono" data-th="Member">${esc(r.memberNumber)}</td>
          <td data-th="Diagnosis">${esc(r.diagnosisText || '—')}${r.isChronic ? ' <span class="ox-badge ox-badge--warn">chronic</span>' : ''}</td>
          <td class="ox-mono" data-th="ICD L1">${esc(r.level1Icd || '—')}${r.level1Name ? `<div class="ox-muted" style="font-size:11px">${esc(r.level1Name)}</div>` : ''}</td>
          <td class="ox-table__num" data-th="Conf.">${r.level1Confidence != null ? Number(r.level1Confidence).toFixed(2) : '—'}</td>
          <td data-th="Provenance"><span class="ox-badge ${settled(r) ? 'ox-badge--electric' : actionable(r) ? 'ox-badge--warn' : 'ox-badge--ok'}">${esc(r.level1Provenance || '—')}</span></td>
          <td class="ox-table__num" data-th="Incurred">${inr(r.incurred)}</td>
          <td data-th="Status">${codingBadge(r.codingStatus)}</td>
          <td class="ox-right">${actionable(r)
            ? `<button class="ox-btn ox-btn--primary ox-btn--sm" data-review="${r.claimCodingId}">Review →</button>`
            : settled(r) ? `<span class="ox-muted" style="font-size:12px">v${r.codingVersion} settled</span>` : ''}</td>
        </tr>`).join('')}</tbody>
    </table></div>`}`;
  panel.querySelectorAll('[data-review]').forEach(b => b.addEventListener('click', () => openReviewDrawer(caseId, b.dataset.review)));
}

function codingBadge(s) {
  const map = { Unmapped: 'ox-badge--danger', InReview: 'ox-badge--warn', AutoAccepted: 'ox-badge--ok', HumanAccepted: 'ox-badge--electric', HumanCorrected: 'ox-badge--electric', Uncertain: 'ox-badge--neutral' };
  return `<span class="ox-badge ${map[s] || 'ox-badge--neutral'}">${esc(s)}</span>`;
}

function openReviewDrawer(caseId, codingId) {
  const r = CODING_ROWS.find(x => String(x.claimCodingId) === String(codingId));
  if (!r) return;
  const backdrop = $('#drawer-backdrop'), drawer = $('#drawer');
  const suggested = r.level1Icd ? `${r.level1Icd} — ${r.level1Name || ''} (conf ${Number(r.level1Confidence).toFixed(2)})` : 'none — the machine could not code this row';
  drawer.innerHTML = `
    <div class="ox-mono-label">CLINICAL REVIEW — ${esc(r.claimNumber)}</div>
    <p class="ox-muted" style="font-size:13px">${esc(r.diagnosisText || '(no diagnosis text)')} · member ${esc(r.memberNumber)} · ${inr(r.incurred)} incurred</p>
    <div class="ox-ana" style="margin:10px 0">
      <div class="ox-ana-row"><span class="ox-muted">AI suggestion</span><span>${esc(suggested)}</span></div>
      <div class="ox-ana-row"><span class="ox-muted">Group</span><span>${esc(r.diseaseGroup || '—')}</span></div>
      <div class="ox-ana-row"><span class="ox-muted">Benefit group</span><span>${esc(r.benefitGroup || '—')}</span></div>
    </div>
    <label class="ox-mono-label" for="rv-icd">CORRECTED ICD L1</label>
    <input class="ox-input" id="rv-icd" placeholder="e.g. E11.9" value="${esc(r.level1Icd || '')}">
    <label class="ox-mono-label" for="rv-name">DISEASE NAME</label>
    <input class="ox-input" id="rv-name" placeholder="e.g. Type 2 Diabetes Mellitus" value="${esc(r.level1Name || '')}">
    <label class="ox-mono-label" for="rv-notes">REVIEWER NOTES</label>
    <input class="ox-input" id="rv-notes" placeholder="recorded permanently with the decision">
    <div class="ox-banner ox-banner--info" style="margin-top:10px">Corrections join the golden dataset — the same diagnosis text codes automatically next time.</div>
    <div class="ox-banner ox-banner--error ox-hide" id="rv-error" style="margin-top:8px"></div>
    <div class="ox-row ox-row--wrap" style="gap:8px;margin-top:12px">
      <button class="ox-btn ox-btn--primary" id="rv-correct">Save correction</button>
      ${r.level1Icd ? '<button class="ox-btn ox-btn--ghost" id="rv-accept">Accept AI coding</button>' : ''}
      <button class="ox-btn ox-btn--ghost" data-act="MarkedUncertain">Mark uncertain</button>
      <button class="ox-btn ox-btn--ghost" data-act="NeedMoreInfo">Need more info</button>
    </div>`;
  backdrop.classList.remove('ox-hide'); drawer.classList.remove('ox-hide');
  requestAnimationFrame(() => { backdrop.classList.add('open'); drawer.classList.add('open'); });
  backdrop.onclick = closeDrawer;
  const decide = async (decision, icd, name) => {
    const err = $('#rv-error'); err.classList.add('ox-hide');
    try {
      const res = await post(`/api/v1/cases/${caseId}/coding/${codingId}/review`, { decision, icdLevel1: icd, diseaseName: name, comments: $('#rv-notes').value });
      closeDrawer();
      if (res.exceptionsResolved) { await post(`/api/v1/cases/${caseId}/pipeline/start`); }
      route();
    } catch (ex) { err.textContent = ex.message; err.classList.remove('ox-hide'); }
  };
  $('#rv-correct').addEventListener('click', () => decide('Corrected', $('#rv-icd').value.trim(), $('#rv-name').value.trim()));
  const acc = $('#rv-accept');
  if (acc) acc.addEventListener('click', () => decide('Accepted', null, null));
  drawer.querySelectorAll('[data-act]').forEach(b => b.addEventListener('click', () => decide(b.dataset.act, null, null)));
}

// ── view: mapping workbench (column → parameter, workflow §6.2 / tech §5.3) ─
let MAPPING_CASES = [];
async function viewMapping() {
  if (!canDecide()) { $('#view').innerHTML = '<div class="ox-banner ox-banner--error">Underwriters only.</div>'; return; }
  $('#pagetitle').textContent = 'Mapping Workbench';
  $('#view').innerHTML = '<div class="ox-skeleton" style="height:200px"></div>';
  MAPPING_CASES = await api('/api/v1/mapping/cases');
  // ?case=N deep-link (EXC-MAP-* CTAs) → auto-select; otherwise the busiest case
  const wantCase = new URLSearchParams(location.hash.split('?')[1] || '').get('case');
  const sel = wantCase ?? String(MAPPING_CASES[0]?.caseId ?? '');
  if (!sel || MAPPING_CASES.length === 0) {
    $('#view').innerHTML = '<div class="ox-empty-state"><p class="ox-mb-0">No profiled columns yet — run a pipeline first.</p></div>';
    return;
  }
  $('#view').innerHTML = `
    <div class="ox-row ox-row--between ox-row--wrap" style="margin-bottom:12px;gap:8px">
      <select class="ox-select" id="mw-case" style="max-width:460px">${MAPPING_CASES.map(c =>
        `<option value="${c.caseId}" ${String(c.caseId) === String(sel) ? 'selected' : ''}>${esc(c.caseNumber)} — ${esc(c.clientName)} (${esc(c.status)})</option>`).join('')}</select>
      <button class="ox-btn ox-btn--ghost" id="mw-rerun" title="Re-run the pipeline so remapped columns re-standardize">Re-run pipeline</button>
    </div>
    <div id="mw-panel"></div>
    <div class="ox-drawer-backdrop ox-hide" id="drawer-backdrop"></div>
    <aside class="ox-drawer ox-hide" id="drawer"></aside>`;
  $('#mw-case').addEventListener('change', () => location.hash = `#/mapping?case=${$('#mw-case').value}`);
  $('#mw-rerun').addEventListener('click', async () => {
    const btn = $('#mw-rerun'); btn.disabled = true; btn.textContent = 'Running…';
    try { await post(`/api/v1/cases/${sel}/pipeline/start?force=1`); toast('Pipeline re-run complete'); }
    catch (ex) { toast(ex.message, true); }
    btn.disabled = false; btn.textContent = 'Re-run pipeline';
    renderMappingPanel(sel);
  });
  await renderMappingPanel(sel);
}

async function renderMappingPanel(caseId) {
  const panel = $('#mw-panel');
  if (!panel) return;
  panel.innerHTML = '<div class="ox-skeleton" style="height:120px"></div>';
  let data;
  try { data = await api(`/api/v1/mapping/${caseId}`); }
  catch (ex) { panel.innerHTML = `<div class="ox-banner ox-banner--error">Failed to load mapping: ${esc(ex.message)}</div>`; return; }

  const kpis = [
    ['DOCUMENTS', data.counts.documents, ''],
    ['MAPPED', data.counts.mapped, data.counts.unmapped > 0 ? '' : 'ox-kpi--ok'],
    ['UNMAPPED', data.counts.unmapped, data.counts.unmapped > 0 ? 'ox-kpi--danger' : 'ox-kpi--ok'],
    ['CONFLICTS', data.counts.conflicts, data.counts.conflicts > 0 ? 'ox-kpi--danger' : ''],
  ];
  panel.innerHTML = `
    <div class="ox-kpi-row" style="margin-bottom:14px">
      ${kpis.map(([label, val, cls]) => `<div class="ox-kpi ${cls}"><div class="ox-mono-label">${label}</div><div class="ox-kpi-num">${val}</div></div>`).join('')}
    </div>
    ${data.conflicts.length > 0 ? `
      <div class="ox-banner ox-banner--error" style="margin-bottom:12px">
        <strong>${data.conflicts.length} mapping conflict${data.conflicts.length > 1 ? 's' : ''} block standardization</strong> —
        multiple columns claim the same parameter. Pick the winner per conflict.
      </div>
      <div class="ox-card" style="margin-bottom:14px">
        <div class="ox-mono-label">CONFLICT PANEL</div>
        ${data.conflicts.map(cf => {
          const cols = data.documents.flatMap(d => d.columns).filter(c => cf.sourceColumnIds.includes(c.sourceColumnId));
          return `<div class="ox-conflict" data-target="${esc(cf.targetParameter)}">
            <div class="ox-conflict-head"><span class="ox-mono">${esc(cf.targetParameter)}</span><span class="ox-muted" style="font-size:11px">doc ${esc(String(cf.documentId))} · ${esc(cf.docType)}</span><span class="ox-badge ox-badge--danger">${cf.sourceColumnIds.length} claimants</span></div>
            ${cols.map(c => `
              <label class="ox-conflict-row">
                <input type="radio" name="conf-${esc(cf.targetParameter)}" value="${c.sourceColumnId}">
                <span class="ox-mono">#${c.sourceColumnId}</span>
                <span>${esc(c.header)}</span>
                <span class="ox-muted" style="font-size:11px">${esc(String(c.samples?.[0] ?? ''))}</span>
              </label>`).join('')}
            <button class="ox-btn ox-btn--primary ox-btn--sm" style="margin-top:6px" data-resolve="${esc(cf.targetParameter)}">Resolve — winner keeps it</button>
          </div>`;
        }).join('')}
      </div>` : ''}
    ${data.documents.length === 0 ? '<div class="ox-empty-state"><p class="ox-mb-0">No profiled documents on this case.</p></div>' :
      data.documents.map(doc => `
        <div class="ox-card" style="margin-bottom:14px">
          <div class="ox-row ox-row--between ox-row--wrap">
            <div class="ox-mono-label">${esc(doc.docType).toUpperCase()} — ${esc(doc.fileName)}</div>
            <span class="ox-badge ${doc.parseStatus === 'Parsed' ? 'ox-badge--ok' : 'ox-badge--neutral'}">${esc(doc.parseStatus)}</span>
          </div>
          <div class="ox-table-wrap"><table class="ox-table">
            <thead><tr><th>Header</th><th>Type</th><th>Samples</th><th>Target</th><th>Conf.</th><th>Provenance</th><th></th></tr></thead>
            <tbody>${doc.columns.map(col => `
              <tr>
                <td data-th="Header"><span class="ox-mono">${esc(col.header)}</span></td>
                <td data-th="Type">${esc(col.detectedType)}</td>
                <td data-th="Samples" class="ox-muted" style="font-size:11px;max-width:220px;overflow-wrap:anywhere">${esc((col.samples || []).join(' · ')) || '—'}</td>
                <td data-th="Target">${col.targetParameter
                  ? `<span class="ox-mono">${esc(col.targetParameter)}</span>${col.isRequired ? ' <span class="ox-badge ox-badge--vivid">required</span>' : ''}`
                  : '<span class="ox-muted">—</span>'}</td>
                <td data-th="Conf."><span class="ox-badge ${col.provenanceTier === 'HumanConfirmed' ? 'ox-badge--electric' : col.confidence >= 0.9 ? 'ox-badge--ok' : 'ox-badge--warn'}">${col.confidence != null ? Number(col.confidence).toFixed(2) : '—'}</span></td>
                <td data-th="Provenance"><span class="ox-badge ${col.provenanceTier === 'HumanConfirmed' ? 'ox-badge--electric' : 'ox-badge--neutral'}">${esc(col.provenanceTier || '—')}</span></td>
                <td class="ox-right"><button class="ox-btn ox-btn--ghost ox-btn--sm" data-retarget="${col.sourceColumnId}">Retarget</button></td>
              </tr>`).join('')}</tbody>
          </table></div>
        </div>`).join('')}`;

  panel.querySelectorAll('[data-retarget]').forEach(b => b.addEventListener('click', () =>
    openRetargetDrawer(caseId, Number(b.dataset.retarget), data)));
  panel.querySelectorAll('[data-resolve]').forEach(b => b.addEventListener('click', async () => {
    const target = b.dataset.resolve;
    const picked = panel.querySelector(`input[name="conf-${CSS.escape(target)}"]:checked`);
    if (!picked) { toast('Pick the winning column first', true); return; }
    const ids = data.conflicts.find(x => x.targetParameter === target).sourceColumnIds;
    const winner = Number(picked.value);
    const loser = ids.find(id => id !== winner);
    b.disabled = true;
    try {
      await post(`/api/v1/mapping/${caseId}/resolve-conflict`, { winnerSourceColumnId: winner, loserSourceColumnId: loser });
      toast(`Conflict resolved — winner #${winner} keeps ${target}`);
      renderMappingPanel(caseId);
    } catch (ex) { b.disabled = false; toast(ex.message, true); }
  }));
}

function openRetargetDrawer(caseId, sourceColumnId, data) {
  const allCols = data.documents.flatMap(d => d.columns);
  const col = allCols.find(c => c.sourceColumnId === sourceColumnId);
  if (!col) return;
  const backdrop = $('#drawer-backdrop'), drawer = $('#drawer');
  drawer.innerHTML = `
    <div class="ox-mono-label">RETARGET COLUMN — ${esc(col.header)}</div>
    <p class="ox-muted" style="font-size:13px">${esc(col.detectedType)} · ${col.nonEmpty} non-empty · ${col.distinct} distinct · doc ${esc(String(col.samples?.[0] ?? ''))}…</p>
    <div class="ox-ana" style="margin:10px 0">
      <div class="ox-ana-row"><span class="ox-muted">Current target</span><span class="ox-mono">${esc(col.targetParameter || '—')}</span></div>
      <div class="ox-ana-row"><span class="ox-muted">Provenance</span><span>${esc(col.provenanceTier || '—')} (conf ${col.confidence != null ? Number(col.confidence).toFixed(2) : '—'})</span></div>
    </div>
    <label class="ox-mono-label" for="rt-target">TARGET PARAMETER</label>
    <select class="ox-select" id="rt-target">
      <option value="">— none (clear mapping) —</option>
      ${data.parameters.map(p => `<option value="${esc(p)}" ${col.targetParameter === p ? 'selected' : ''}>${esc(p)}</option>`).join('')}
    </select>
    <div class="ox-banner ox-banner--info" style="margin-top:10px">Human decisions pin the mapping — pipeline re-runs keep it and record an audited ColumnMapping row (conf 1.00, HumanConfirmed).</div>
    <div class="ox-banner ox-banner--error ox-hide" id="rt-error" style="margin-top:8px"></div>
    <div class="ox-row ox-row--wrap" style="gap:8px;margin-top:12px">
      <button class="ox-btn ox-btn--primary" id="rt-save">Save decision</button>
      <button class="ox-btn ox-btn--ghost" id="rt-clear">Clear mapping</button>
    </div>`;
  backdrop.classList.remove('ox-hide'); drawer.classList.remove('ox-hide');
  requestAnimationFrame(() => { backdrop.classList.add('open'); drawer.classList.add('open'); });
  backdrop.onclick = closeDrawer;
  const save = async (target) => {
    const err = $('#rt-error'); err.classList.add('ox-hide');
    try {
      await post(`/api/v1/mapping/${caseId}/retarget`, { sourceColumnId, targetParameter: target });
      closeDrawer();
      toast(target ? `Saved — ${col.header} → ${target}` : `Cleared — ${col.header}`);
      renderMappingPanel(caseId);
    } catch (ex) {
      if (ex.conflict && ex.conflict.conflictingSourceColumnId) {
        err.innerHTML = `${esc(ex.message)} <button class="ox-btn ox-btn--danger-ghost ox-btn--sm" id="rt-resolve">Award it to #${ex.conflict.conflictingSourceColumnId} (${esc(ex.conflict.conflictingHeader)})</button>`;
        err.classList.remove('ox-hide');
        $('#rt-resolve').addEventListener('click', async () => {
          try {
            await post(`/api/v1/mapping/${caseId}/resolve-conflict`, { winnerSourceColumnId: ex.conflict.conflictingSourceColumnId, loserSourceColumnId: sourceColumnId });
            closeDrawer();
            toast('Conflict resolved');
            renderMappingPanel(caseId);
          } catch (ex2) { err.textContent = ex2.message; }
        });
      } else { err.textContent = ex.message; err.classList.remove('ox-hide'); }
    }
  };
  $('#rt-save').addEventListener('click', () => save($('#rt-target').value || null));
  $('#rt-clear').addEventListener('click', () => save(null));
}

// ── toast (workbench save feedback) ─────────────────────────────────────────
let TOAST_TIMER = null;
function toast(msg, isError = false) {
  let el = $('#ox-toast');
  if (!el) {
    el = document.createElement('div');
    el.id = 'ox-toast';
    el.setAttribute('role', 'status');
    document.body.appendChild(el);
  }
  el.className = `ox-toast${isError ? ' ox-toast--error' : ''}`;
  el.textContent = msg;
  requestAnimationFrame(() => el.classList.add('open'));
  clearTimeout(TOAST_TIMER);
  TOAST_TIMER = setTimeout(() => el.classList.remove('open'), 3200);
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
          <td class="ox-right">${isClinical(e.code)
            ? `<a class="ox-btn ox-btn--primary ox-btn--sm" href="#/coding?case=${e.caseId}">Resolve →</a>`
            : isMappingExc(e.code)
            ? `<a class="ox-btn ox-btn--primary ox-btn--sm" href="#/mapping?case=${e.caseId}">Resolve →</a>`
            : e.caseId ? `<a class="ox-btn ox-btn--ghost ox-btn--sm" href="#/case/${e.caseId}">Open →</a>` : ''}</td>
        </tr>`).join('')}</tbody>
    </table></div>`}`;
}

// clinical codes resolve in the coding workbench; mapping codes in the mapping workbench
const isClinical = (code) => code === 'EXC-CLIN-UNMAPPED' || code === 'EXC-CLIN-REVIEW';
const isMappingExc = (code) => typeof code === 'string' && code.startsWith('EXC-MAP-');

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

// ── view: admin (masters, AI gateway, audit, stats) ────────────────────────
async function viewAdmin() {
  if (!isAdmin()) { $('#view').innerHTML = '<div class="ox-banner ox-banner--error">Admins only.</div>'; return; }
  $('#pagetitle').textContent = 'Admin';
  $('#view').innerHTML = '<div class="ox-skeleton" style="height:200px"></div>';
  const [stats, masters, ai, audit, rules] = await Promise.all([
    api('/api/v1/admin/stats').catch(() => null),
    api('/api/v1/admin/masters').catch(() => []),
    api('/api/v1/admin/ai-status').catch(() => null),
    api('/api/v1/admin/audit?limit=50').catch(() => []),
    api('/api/v1/admin/guardrail-rules').catch(() => []),
  ]);

  const masterRows = (masters || []).map(m => `
    <tr><td class="ox-mono" data-th="Version">${esc(m.versionRef)}</td>
    <td data-th="Status">${badge(m.status === 'Active' ? 'AutoQuoted' : 'Draft').replace('AutoQuoted', esc(m.status)).replace('Draft', esc(m.status))}</td>
    <td data-th="From">${new Date(m.effectiveFromUtc).toLocaleDateString('en-IN')}</td>
    <td data-th="To">${m.effectiveToUtc ? new Date(m.effectiveToUtc).toLocaleDateString('en-IN') : '—'}</td></tr>`).join('');

  const auditRows = (audit || []).map(a => `
    <tr><td data-th="When">${new Date(a.createdAtUtc).toLocaleString('en-IN')}</td>
    <td class="ox-mono" data-th="Entity">${esc(a.entityType || '—')}${a.entityId ? '#' + a.entityId : ''}</td>
    <td data-th="Action">${esc(a.action || '—')}</td>
    <td data-th="Actor">${esc(a.actorType || '')} ${esc(a.actorId || '')}</td></tr>`).join('');

  $('#view').innerHTML = `
    ${stats ? `
    <div class="ox-kpi-row">
      <div class="ox-kpi ox-kpi--primary"><div class="ox-mono-label">TOTAL CASES</div><div class="ox-kpi-num">${stats.totalCases}</div></div>
      <div class="ox-kpi ox-kpi--ok"><div class="ox-mono-label">STRAIGHT-THROUGH %</div><div class="ox-kpi-num">${stats.straightThroughRatePct}%</div></div>
      <div class="ox-kpi ox-kpi--warn"><div class="ox-mono-label">REFERRAL %</div><div class="ox-kpi-num">${stats.referralRatePct}%</div></div>
    </div>` : ''}
    <div class="ox-grid-2" style="margin-top:16px">
      <div class="ox-card">
        <div class="ox-mono-label">AI GATEWAY — ${esc(ai?.mode || 'deterministic-only')}</div>
        <div class="ox-ana">
          ${[['Extraction agent', ai?.extraction], ['Mapping agent', ai?.mapping], ['Clinical coding agent', ai?.clinical], ['Narrative agent', ai?.narrative]]
            .map(([k, v]) => `<div class="ox-ana-row"><span class="ox-muted">${k}</span><span>${v ? '● wired' : '○ not wired'}</span></div>`).join('')}
        </div>
        <p class="ox-muted" style="font-size:12px">${esc(ai?.note || '')}</p>
      </div>
      <div class="ox-card">
        <div class="ox-mono-label">PRICING MASTERS</div>
        <div class="ox-table-wrap"><table class="ox-table"><thead><tr><th>Version</th><th>Status</th><th>From</th><th>To</th></tr></thead><tbody>${masterRows}</tbody></table></div>
        <div class="ox-hr"></div>
        <div class="ox-mono-label">EXCEL MASTERS UPLOAD</div>
        <p class="ox-muted" style="font-size:12px">Sheets: <span class="ox-mono">BenefitCosting</span> · <span class="ox-mono">Commission</span> · <span class="ox-mono">StandardLoading</span>. Rows upsert into the active master version.</p>
        <div class="ox-row" style="margin-top:8px;gap:8px;align-items:center">
          <input type="file" id="mx-file" accept=".xlsx,.csv" class="ox-input" style="max-width:280px">
          <button class="ox-btn ox-btn--primary ox-btn--sm" id="mx-upload">Upload masters</button>
          <span id="mx-result" class="ox-muted" style="font-size:12px"></span>
        </div>
      </div>
    </div>
    <div class="ox-card" style="margin-top:16px">
      <div class="ox-mono-label">GUARDRAIL RULE CONFIGURATOR — ${rules.length} RULES</div>
      <div class="ox-table-wrap" style="margin-top:10px"><table class="ox-table">
        <thead><tr><th>Code</th><th>Category</th><th>Metric</th><th>Rule</th><th>Severity</th><th>Enabled</th></tr></thead>
        <tbody>${rules.map(r => `
          <tr>
            <td class="ox-mono">${esc(r.ruleCode)}</td>
            <td>${esc(r.category)}</td>
            <td class="ox-mono" style="font-size:11px">${esc(r.metricSelector)}</td>
            <td style="font-size:12px">${esc(r.operator)} ${r.threshold} — ${esc((r.messageTemplate || '').slice(0, 60))}…</td>
            <td><span class="ox-badge ${r.severity === 'HardStop' ? 'ox-badge--error' : 'ox-badge--warn'}">${esc(r.severity)}</span></td>
            <td><button class="ox-btn ox-btn--sm" data-rule="${esc(r.ruleCode)}" style="${r.isEnabled ? '' : 'opacity:.5'}">${r.isEnabled ? 'ON' : 'OFF'}</button></td>
          </tr>`).join('')}</tbody>
      </table></div>
      <p class="ox-muted" style="font-size:12px">Rules are data — edits apply on the next pipeline run. Toggle to disable a rule without deleting it.</p>
    </div>
    <div class="ox-card" style="margin-top:16px">
      <div class="ox-mono-label">AUDIT LOG (LAST 50)</div>
      ${auditRows ? `<div class="ox-table-wrap"><table class="ox-table"><thead><tr><th>When</th><th>Entity</th><th>Action</th><th>Actor</th></tr></thead><tbody>${auditRows}</tbody></table></div>` : '<p class="ox-muted">No audit entries yet.</p>'}
    </div>`;
  $('#view').querySelectorAll('[data-rule]').forEach(b => b.addEventListener('click', async () => {
    try {
      const res = await post(`/api/v1/admin/guardrail-rules/${b.dataset.rule}/toggle`, {});
      if (res.error) throw new Error(res.error);
      route();
    } catch (ex) { alert('Toggle failed: ' + ex.message); }
  }));
  $('#mx-upload').addEventListener('click', async () => {
    const f = $('#mx-file').files[0];
    if (!f) { alert('Choose a masters workbook first.'); return; }
    const fd = new FormData(); fd.append('file', f);
    try {
      const res = await fetch('/api/v1/admin/masters/upload', { method: 'POST', body: fd, credentials: 'same-origin' });
      const d = await res.json();
      if (!res.ok) throw new Error(d.error || res.statusText);
      const parts = Object.entries(d.sheets || {}).map(([k, v]) => `${k}: ${v >= 0 ? v + ' rows' : 'skipped'}`);
      $('#mx-result').textContent = parts.join(' · ') || 'no sheets matched';
    } catch (ex) { alert('Upload failed: ' + ex.message); }
  });
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
