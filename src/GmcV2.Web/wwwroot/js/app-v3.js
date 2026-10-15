/* GMC v3 — role-first shell (Dhrumil's approved prototype UX) wired to the live engine.
   Loads AFTER views-analytics.js (viewAnalytics/viewBenefits/viewFinalQuote/viewStandardize
   are globals). Shared helpers exposed by app.js v19+: window.$ esc inr api canDecide isAdmin.
   app.js itself is NOT loaded here — v3 is self-contained. */
(() => {
'use strict';
const $ = (s) => document.querySelector(s);
const esc = (v) => String(v ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&', '<': '<', '>': '>', '"': '"', "'": '&#39;' }[c]));
const inr = (v) => v == null || v === '' ? '—' : '₹' + Number(v).toLocaleString('en-IN', { maximumFractionDigits: 2 });
const pct = (v) => v == null ? '—' : (Number(v)).toFixed(1) + '%';

const api = async (path, opts = {}) => {
  const r = await fetch(path, { headers: { 'Content-Type': 'application/json' }, credentials: 'same-origin', ...opts });
  if (!r.ok) {
    let msg = r.statusText, body = null;
    try { const j = await r.json(); if (j.error) msg = j.error; body = j; } catch { /* ignore */ }
    const ex = new Error(msg); ex.body = body; ex.status = r.status; throw ex;
  }
  const ct = r.headers.get('content-type') || '';
  return ct.includes('json') ? r.json() : r.text();
};
const post = (path, body) => api(path, { method: 'POST', body: JSON.stringify(body ?? {}) });

let ME = null;
let CASE = null;          // active case context for the stepper
let QUEUE = [];           // /api/v1/queue rows
let UW_FILTER = 'all', UW_SEARCH = '';
let SQ_FILTER = 'all', SQ_SEARCH = '';
const canDecideRole = () => ['Underwriter', 'SeniorUnderwriter', 'ChiefUnderwriter', 'Admin'].includes(ME?.role);
const isAdminRole = () => ME?.role === 'Admin';

const ROLE_LABEL = { SalesPerson: 'Sales Agent', SalesAgent: 'Sales Agent', Underwriter: 'Underwriter', SeniorUnderwriter: 'Senior UW', ChiefUnderwriter: 'VP / Chief UW', Admin: 'Admin' };
const STEP_NAMES = ['Upload', 'Mapping', 'Standardize', 'Disease', 'Analytics', 'Guardrails', 'Pricing', 'Benefits', 'Final Quote'];

const root = () => $('#v3-root');

/* ── screen: login rolecards ── */
function renderLogin() {
  $('#v3-case-pill').textContent = 'Not logged in';
  $('#v3-role-pill').textContent = '—';
  $('#v3-queue-btn').style.display = 'none';
  $('#v3-logout-btn').style.display = 'none';
  root().innerHTML = `
  <div class="v3-loginbox">
    <h1 class="ox-display">Enterprise Login</h1>
    <p class="v3-small">Select your operational role to access the Opexminds Underwriting & Quoting Suite.</p>
    <div class="v3-rolecards">
      <div class="v3-rolecard" id="lc-sales">
        <h2 class="ox-display">Sales / Insurance Agent</h2>
        <p class="v3-small">Upload case documentation, review AI benefit extractions, configure census exposures, and structure quotes with integrated commission guardrails.</p>
        <button class="ox-btn ox-btn--primary" style="margin-top:12px">Login as Sales</button>
      </div>
      <div class="v3-rolecard" id="lc-uw">
        <h2 class="ox-display">Underwriter / Risk Actuary</h2>
        <p class="v3-small">Access the unified underwriting queue, inspect credibility-adjusted burn models, govern pricing masters, and manage referrals.</p>
        <button class="ox-btn ox-btn--primary" style="margin-top:12px">Login as Underwriter</button>
      </div>
    </div>
    <div class="v3-pill-note" style="margin-top:20px">Whitelabel B2B architecture configured for core carrier deployment.</div>
    <div class="ox-banner ox-banner--error ox-hide" id="v3-login-err" style="margin-top:12px"></div>
  </div>`;
  $('#lc-sales').addEventListener('click', () => quickLogin('sales@opexminds.com', 'Sales@12345'));
  $('#lc-uw').addEventListener('click', () => quickLogin('uw@opexminds.com', 'Uw@12345'));
}

async function quickLogin(email, password) {
  const err = $('#v3-login-err'); err.classList.add('ox-hidden');
  try {
    await post('/api/v1/auth/login', { email, password });
    ME = await api('/api/v1/auth/me');
    enter();
  } catch (ex) {
    err.textContent = ex.message; err.classList.remove('ox-hidden');
  }
}

/* ── entry after auth ── */
function enter() {
  $('#v3-role-pill').textContent = ROLE_LABEL[ME.role] || ME.role;
  $('#v3-logout-btn').style.display = '';
  $('#v3-queue-btn').style.display = '';
  if (ME.role === 'SalesPerson' || ME.role === 'SalesAgent') renderSalesQueue();
  else renderUWQueue();
}

/* ── screen: UW queue ── */
async function renderUWQueue() {
  root().innerHTML = '<div class="ox-skeleton" style="height:220px"></div>';
  $('#v3-case-pill').textContent = 'UW Governance';
  try { QUEUE = await api('/api/v1/queue'); } catch (ex) { root().innerHTML = errBanner(ex); return; }
  const uw = QUEUE.filter(q => !q.isSalesOnly);
  const totals = {
    total: QUEUE.length,
    auto: QUEUE.filter(q => q.isAutoQuote).length,
    refer: QUEUE.filter(q => q.isReferral).length,
  };
  root().innerHTML = `
  <div class="ox-row" style="justify-content:space-between;align-items:flex-start;margin-bottom:20px">
    <div>
      <h1 class="ox-display" style="font-size:26px;margin:0">Underwriting Queue</h1>
      <p class="v3-small" style="margin:4px 0 0">Centralized governance for Auto Quotes, Guardrail Exceptions, Commercial Discounts, and Manual Referrals.</p>
    </div>
    <div class="ox-row">
      ${isAdminRole() ? '<button class="ox-btn ox-btn--ghost" id="v3-admin-btn">⚙ Master Config</button>' : ''}
      <button class="ox-btn ox-btn--primary" id="v3-new-quote">+ New Quote</button>
    </div>
  </div>
  <div class="v3-grid" style="margin-bottom:18px">
    <div class="v3-card v3-c4"><div class="v3-small">Total Cases</div><div class="v3-metric">${totals.total}</div></div>
    <div class="v3-card v3-c4"><div class="v3-small">Auto Quoted</div><div class="v3-metric">${totals.auto}</div></div>
    <div class="v3-card v3-c4"><div class="v3-small">Guardrail Referrals</div><div class="v3-metric">${totals.refer}</div></div>
  </div>
  <div class="v3-card">
    <div class="ox-row" style="justify-content:space-between;margin-bottom:14px">
      <div class="v3-queue-tabs" style="margin:0">
        ${[['all', 'All'], ['autoquote', 'Auto Quotes'], ['refer', 'Guardrail Failures'], ['discount', 'Discounts'], ['manual', 'Manual Referrals']]
          .map(([k, l]) => `<button class="v3-queue-tab ${UW_FILTER === k ? 'active' : ''}" data-f="${k}">${l}</button>`).join('')}
      </div>
      <input class="ox-input" id="v3-uw-search" placeholder="Search case or client..." style="max-width:260px" value="${esc(UW_SEARCH)}"/>
    </div>
    <div class="ox-table-wrap"><table class="ox-table">
      <thead><tr><th>Case ID</th><th>Client</th><th>Stage</th><th>Lives</th><th>Premium</th><th>Reason</th><th>Status</th><th>Priority</th><th></th></tr></thead>
      <tbody id="v3-uw-body"></tbody>
    </table></div>
  </div>`;
  $('#v3-uw-search').addEventListener('input', (e) => { UW_SEARCH = e.target.value; renderUWRows(); });
  root().querySelectorAll('.v3-queue-tab').forEach(b => b.addEventListener('click', () => {
    UW_FILTER = b.dataset.f;
    root().querySelectorAll('.v3-queue-tab').forEach(x => x.classList.toggle('active', x === b));
    renderUWRows();
  }));
  $('#v3-new-quote').addEventListener('click', () => openStepper(null));
  if ($('#v3-admin-btn')) $('#v3-admin-btn').addEventListener('click', renderAdminConsole);
  renderUWRows();
}

function renderUWRows() {
  const body = $('#v3-uw-body'); if (!body) return;
  let rows = QUEUE;
  if (UW_FILTER === 'autoquote') rows = rows.filter(q => q.isAutoQuote);
  else if (UW_FILTER === 'refer') rows = rows.filter(q => q.isReferral);
  else if (UW_FILTER === 'discount') rows = rows.filter(q => String(q.reason || '').toLowerCase().includes('discount'));
  else if (UW_FILTER === 'manual') rows = rows.filter(q => q.priority === 'High');
  if (UW_SEARCH) {
    const s = UW_SEARCH.toLowerCase();
    rows = rows.filter(q => (q.caseNumber || '').toLowerCase().includes(s) || (q.clientName || '').toLowerCase().includes(s));
  }
  body.innerHTML = rows.map(q => `
    <tr>
      <td class="ox-mono">${esc(q.caseNumber)}</td>
      <td>${esc(q.clientName)}</td>
      <td>${esc(q.currentStage || '—')}</td>
      <td class="ox-num">${q.lives || '—'}</td>
      <td class="ox-num">${inr(q.premium)}</td>
      <td class="v3-small" style="max-width:280px;white-space:normal">${esc(q.reason)}</td>
      <td><span class="ox-badge ${q.isAutoQuote ? 'ox-badge--ok' : q.isReferral ? 'ox-badge--warn' : 'ox-badge--neutral'}">${esc(q.status)}</span></td>
      <td>${esc(q.priority)}</td>
      <td class="ox-right">${q.isReferral
        ? `<button class="ox-btn ox-btn--primary ox-btn--sm" data-wb="${q.caseId}">Review</button>`
        : `<button class="ox-btn ox-btn--ghost ox-btn--sm" data-open="${q.caseId}">View</button>`}</td>
    </tr>`).join('') || '<tr><td colspan="9" class="v3-small">No cases match.</td></tr>';
  body.querySelectorAll('[data-wb]').forEach(b => b.addEventListener('click', () => openWorkbench(Number(b.dataset.wb))));
  body.querySelectorAll('[data-open]').forEach(b => b.addEventListener('click', () => openStepper(Number(b.dataset.open))));
}

/* ── screen: Sales queue ── */
async function renderSalesQueue() {
  root().innerHTML = '<div class="ox-skeleton" style="height:220px"></div>';
  $('#v3-case-pill').textContent = 'Sales Portal';
  try { QUEUE = await api('/api/v1/queue'); } catch (ex) { root().innerHTML = errBanner(ex); return; }
  const totals = {
    total: QUEUE.length,
    quoted: QUEUE.filter(q => q.premium != null).length,
    uw: QUEUE.filter(q => q.isReferral).length,
  };
  root().innerHTML = `
  <div class="ox-row" style="justify-content:space-between;align-items:flex-start;margin-bottom:20px">
    <div>
      <h1 class="ox-display" style="font-size:26px;margin:0">Sales Quote Queue</h1>
      <p class="v3-small" style="margin:4px 0 0">Manage generated quotations, active underwriting referrals, and discount requests.</p>
    </div>
    <button class="ox-btn ox-btn--primary" id="v3-new-quote">+ New Quote</button>
  </div>
  <div class="v3-grid" style="margin-bottom:18px">
    <div class="v3-card v3-c4"><div class="v3-small">Total Cases</div><div class="v3-metric">${totals.total}</div></div>
    <div class="v3-card v3-c4"><div class="v3-small">Quoted</div><div class="v3-metric">${totals.quoted}</div></div>
    <div class="v3-card v3-c4"><div class="v3-small">Under UW Review</div><div class="v3-metric">${totals.uw}</div></div>
  </div>
  <div class="v3-card">
    <div class="ox-row" style="justify-content:space-between;margin-bottom:14px">
      <div class="v3-queue-tabs" style="margin:0">
        ${[['all', 'All'], ['quote', 'Quoted'], ['referral', 'Referral / UW'], ['approved', 'Approved']]
          .map(([k, l]) => `<button class="v3-queue-tab ${SQ_FILTER === k ? 'active' : ''}" data-f="${k}">${l}</button>`).join('')}
      </div>
      <input class="ox-input" id="v3-sq-search" placeholder="Search client..." style="max-width:260px" value="${esc(SQ_SEARCH)}"/>
    </div>
    <div class="ox-table-wrap"><table class="ox-table">
      <thead><tr><th>Case ID</th><th>Client</th><th>Lives</th><th>Premium</th><th>Quote Status</th><th>UW Status</th><th>Updated</th><th></th></tr></thead>
      <tbody id="v3-sq-body"></tbody>
    </table></div>
  </div>`;
  $('#v3-sq-search').addEventListener('input', (e) => { SQ_SEARCH = e.target.value; renderSQRows(); });
  root().querySelectorAll('.v3-queue-tab').forEach(b => b.addEventListener('click', () => {
    SQ_FILTER = b.dataset.f;
    root().querySelectorAll('.v3-queue-tab').forEach(x => x.classList.toggle('active', x === b));
    renderSQRows();
  }));
  $('#v3-new-quote').addEventListener('click', () => openStepper(null));
  renderSQRows();
}

function renderSQRows() {
  const body = $('#v3-sq-body'); if (!body) return;
  let rows = QUEUE;
  if (SQ_FILTER === 'quote') rows = rows.filter(q => q.premium != null);
  else if (SQ_FILTER === 'referral') rows = rows.filter(q => q.isReferral);
  else if (SQ_FILTER === 'approved') rows = rows.filter(q => q.status === 'Approved' || q.status === 'ApprovedWithChanges');
  if (SQ_SEARCH) {
    const s = SQ_SEARCH.toLowerCase();
    rows = rows.filter(q => (q.caseNumber || '').toLowerCase().includes(s) || (q.clientName || '').toLowerCase().includes(s));
  }
  body.innerHTML = rows.map(q => `
    <tr>
      <td class="ox-mono">${esc(q.caseNumber)}</td>
      <td>${esc(q.clientName)}</td>
      <td class="ox-num">${q.lives || '—'}</td>
      <td class="ox-num">${inr(q.premium)}</td>
      <td>${q.premium != null ? '<span class="ox-badge ox-badge--ok">Quote Generated</span>' : '<span class="ox-badge ox-badge--neutral">Draft</span>'}</td>
      <td><span class="ox-badge ${q.isReferral ? 'ox-badge--warn' : 'ox-badge--neutral'}">${esc(q.status)}</span></td>
      <td class="v3-small">${(q.updatedAt || '').slice(0, 10)}</td>
      <td class="ox-right"><button class="ox-btn ox-btn--ghost ox-btn--sm" data-open="${q.caseId}">Open</button></td>
    </tr>`).join('') || '<tr><td colspan="8" class="v3-small">No cases match.</td></tr>';
  body.querySelectorAll('[data-open]').forEach(b => b.addEventListener('click', () => openStepper(Number(b.dataset.open))));
}

const errBanner = (ex) => `<div class="ox-banner ox-banner--error">Failed to load: ${esc(ex.message)}</div>`;

/* ── screen: 9-step stepper ── */
async function openStepper(caseId) {
  root().innerHTML = '<div class="ox-skeleton" style="height:220px"></div>';
  if (caseId) {
    try { CASE = await api(`/api/v1/cases/${caseId}`); }
    catch (ex) { root().innerHTML = errBanner(ex); return; }
  } else CASE = null;
  $('#v3-case-pill').textContent = CASE ? CASE.caseNumber : 'New Quote';
  renderStepperShell(1);
}

function renderStepperShell(step) {
  const s = CASE?.status;
  root().innerHTML = `
  <div class="v3-stepper" id="v3-stepper">${STEP_NAMES.map((n, i) =>
    `<div class="v3-step ${step === i + 1 ? 'active' : step > i + 1 ? 'done' : ''}" data-step="${i + 1}"><span>${String(i + 1).padStart(2, '0')}</span><b>${n}</b></div>`).join('')}</div>
  <section class="v3-panel" id="v3-panel"></section>
  <div class="v3-footerbar" id="v3-navbtns"></div>`;
  root().querySelectorAll('.v3-step').forEach(el => el.addEventListener('click', () => showStep(Number(el.dataset.step))));
  showStep(step);
}

function navBtns(step) {
  const el = $('#v3-navbtns'); if (!el) return;
  const back = step > 1 ? `<button class="ox-btn ox-btn--ghost" data-go="${step - 1}">← ${STEP_NAMES[step - 2]}</button>` : '<span></span>';
  const next = step < 9 ? `<button class="ox-btn ox-btn--primary" data-go="${step + 1}">${STEP_NAMES[step]} →</button>` : '';
  el.innerHTML = back + next;
  el.querySelectorAll('[data-go]').forEach(b => b.addEventListener('click', () => showStep(Number(b.dataset.go))));
}

async function showStep(n) {
  root().querySelectorAll('.v3-step').forEach(el =>
    el.classList.toggle('active', Number(el.dataset.step) === n) || el.classList.toggle('done', Number(el.dataset.step) < n));
  navBtns(n);
  const panel = $('#v3-panel');
  if (!CASE) { await newCaseStep(n, panel); return; }
  const id = CASE.caseId;
  try {
    if (n === 1) return renderUpload(panel);
    if (n === 2) return renderMapping(panel, id);
    if (n === 3) return await renderStandardize(panel, id);
    if (n === 4) return await renderCoding(panel, id);
    if (n === 5) return await renderAnalytics(panel, id);
    if (n === 6) return await renderGuardrails(panel, id);
    if (n === 7) return await renderPricing(panel, id);
    if (n === 8) return await renderBenefits(panel, id);
    if (n === 9) return await renderFinalQuote(panel, id);
  } catch (ex) { panel.innerHTML = errBanner(ex); }
}

/* ── step 1: upload (new case form + file inputs) ── */
async function newCaseStep(n, panel) {
  if (n !== 1) { panel.innerHTML = '<div class="ox-banner ox-banner--warn">Create the case first — upload documents in step 1.</div>'; return; }
  panel.innerHTML = `
  <h1 class="ox-display" style="font-size:24px">1. New Quote — Upload Case Data</h1>
  <p class="v3-small">Create the case, then upload member census, historical claims, expiring policy, and proposed RFQ documents. Parsing and pipeline start automatically.</p>
  <div class="v3-grid">
    <div class="v3-card v3-c6">
      <h3 class="ox-display">Case Profile</h3>
      <label class="ox-mono-label">CLIENT NAME *</label><input class="ox-input" id="nc-client" placeholder="Acme India Services Pvt Ltd"/>
      <label class="ox-mono-label" style="margin-top:10px">BUSINESS TYPE</label>
      <select class="ox-input" id="nc-type"><option>Rollover</option><option>Fresh</option></select>
      <label class="ox-mono-label" style="margin-top:10px">INDUSTRY</label><input class="ox-input" id="nc-industry" value="IT"/>
      <label class="ox-mono-label" style="margin-top:10px">TPA</label><input class="ox-input" id="nc-tpa" value="HS"/>
      <button class="ox-btn ox-btn--primary" id="nc-create" style="margin-top:14px">Create Case →</button>
    </div>
    <div class="v3-card v3-c6">
      <h3 class="ox-display">Documents</h3>
      ${[['nc-enroll', 'Enrollment / Census', '.csv,.xlsx,.txt'], ['nc-claims', 'Claims Experience', '.csv,.xlsx,.txt'], ['nc-expiring', 'Expiring Policy', '.csv,.txt,.pdf'], ['nc-proposed', 'Proposed RFQ', '.csv,.txt,.pdf']]
        .map(([id, label, accept]) => `
      <label class="ox-mono-label" style="margin-top:10px">${label}</label>
      <input class="ox-input" type="file" id="${id}" accept="${accept}"/>
      <div class="v3-small" id="${id}-status" style="margin-top:4px">No file loaded</div>`).join('')}
      <div class="v3-pill-note" style="margin-top:14px">Upload all four — the pipeline auto-starts when required docs land.</div>
    </div>
  </div>`;
  $('#nc-create').addEventListener('click', async () => {
    const clientName = $('#nc-client').value.trim();
    if (!clientName) return alert('Client name required');
    try {
      const res = await post('/api/v1/cases', { clientName, businessType: $('#nc-type').value, businessSubType: 'Main', industry: $('#nc-industry').value, tpa: $('#nc-tpa').value });
      CASE = await api(`/api/v1/cases/${res.caseId}`);
      $('#v3-case-pill').textContent = CASE.caseNumber;
      await uploadAll(res.caseId);
      renderStepperShell(1); // re-render with case context
    } catch (ex) { alert('Create failed: ' + ex.message); }
  });
  for (const [id] of [['nc-enroll'], ['nc-claims'], ['nc-expiring'], ['nc-proposed']]) {
    $(`#${id}`).addEventListener('change', (e) => {
      $(`#${id}-status`).textContent = e.target.files[0] ? `Selected: ${e.target.files[0].name}` : 'No file loaded';
    });
  }
}

async function uploadAll(caseId) {
  const map = [['nc-enroll', 'Enrollment'], ['nc-claims', 'ClaimsRegister'], ['nc-expiring', 'ExpiringPolicy'], ['nc-proposed', 'Rfq']];
  for (const [inputId, docType] of map) {
    const inp = $(`#${inputId}`);
    if (!inp?.files?.[0]) continue;
    const fd = new FormData(); fd.append('file', inp.files[0]);
    const r = await fetch(`/api/v1/cases/${caseId}/documents/upload?docType=${docType}`, { method: 'POST', body: fd, credentials: 'same-origin' });
    if (!r.ok) throw new Error(`${docType} upload failed: ${r.statusText}`);
    $(`#${inputId}-status`).textContent = '✓ Uploaded & parsed';
  }
  // auto-start the pipeline once docs are in
  try { await post(`/api/v1/cases/${caseId}/pipeline/start`); } catch { /* may need force later */ }
}

function renderUpload(panel) {
  const docs = CASE?.documents || [];
  panel.innerHTML = `
  <h1 class="ox-display" style="font-size:24px">1. Upload Case Data & Documents</h1>
  <p class="v3-small">Current documents on ${esc(CASE.caseNumber)} — replace any by uploading again. Pipeline runs automatically.</p>
  <div class="v3-grid">
    <div class="v3-card v3-c12">
      <div class="ox-table-wrap"><table class="ox-table">
        <thead><tr><th>Doc Type</th><th>File</th><th>Status</th><th>Replace</th></tr></thead>
        <tbody>${docs.map(d => `
          <tr><td>${esc(d.docType)}</td><td class="ox-mono">${esc(d.fileName)}</td>
          <td><span class="ox-badge ${d.parseStatus === 'Parsed' ? 'ox-badge--ok' : 'ox-badge--warn'}">${esc(d.parseStatus)}</span></td>
          <td><input type="file" class="ox-input" style="padding:6px" data-replace="${d.docType}" accept=".csv,.xlsx,.txt,.pdf"/></td></tr>`).join('')
          || '<tr><td colspan="4" class="v3-small">No documents yet.</td></tr>'}</tbody>
      </table></div>
      <div class="ox-row" style="margin-top:14px;justify-content:space-between">
        <button class="ox-btn ox-btn--ghost" id="up-more">+ Add another document</button>
        <button class="ox-btn ox-btn--primary" id="up-run">Run Pipeline ▶</button>
      </div>
      <div class="v3-small" id="up-status" style="margin-top:8px"></div>
    </div>
  </div>`;
  $('#up-run').addEventListener('click', async () => {
    $('#up-status').textContent = 'Pipeline running — this takes ~4-5 minutes on real documents…';
    try {
      const res = await post(`/api/v1/cases/${CASE.caseId}/pipeline/start?force=1`);
      $('#up-status').textContent = `Pipeline ${res.status}: ${res.stageCount} stages recorded. Continue to Mapping →`;
    } catch (ex) { $('#up-status').textContent = 'Failed: ' + ex.message; }
  });
  panel.querySelectorAll('[data-replace]').forEach(inp => inp.addEventListener('change', async (e) => {
    if (!e.target.files[0]) return;
    const fd = new FormData(); fd.append('file', e.target.files[0]);
    const r = await fetch(`/api/v1/cases/${CASE.caseId}/documents/upload?docType=${e.target.dataset.replace}`, { method: 'POST', body: fd, credentials: 'same-origin' });
    if (r.ok) showStep(1); else alert('Upload failed');
  }));
  $('#up-more').addEventListener('click', () => openStepper(null));
}

/* ── step 2: mapping ── */
async function renderMapping(panel, id) {
  panel.innerHTML = '<div class="ox-skeleton" style="height:160px"></div>';
  let m;
  try { m = await api(`/api/v1/mapping/${id}`); } catch (ex) { panel.innerHTML = errBanner(ex); return; }
  const mapCard = (title, cols) => `
    <div class="v3-card v3-c6"><h3 class="ox-display">${title}</h3>
      <div class="ox-table-wrap"><table class="ox-table">
        <thead><tr><th>Source Column</th><th>Target Field</th><th>Confidence</th></tr></thead>
        <tbody>${(cols || []).map(c => `
          <tr><td class="ox-mono">${esc(c.sourceColumn)}</td><td>${esc(c.targetField || '—')}</td>
          <td class="ox-num">${c.confidence != null ? Math.round(c.confidence * 100) + '%' : '—'}</td></tr>`).join('')
          || '<tr><td colspan="3" class="v3-small">—</td></tr>'}</tbody>
      </table></div>
    </div>`;
  panel.innerHTML = `
  <h1 class="ox-display" style="font-size:24px">2. Schema Mapping</h1>
  <p class="v3-small">Map raw columns to the Opexminds canonical schema. Adjust via the full workbench for conflicts.</p>
  <div class="v3-grid">${mapCard('Enrollment Schema', m.enrollmentColumns)}${mapCard('Claims Schema', m.claimsColumns)}</div>
  <div class="ox-row" style="margin-top:14px"><button class="ox-btn ox-btn--ghost" id="mp-workbench">Open full Mapping Workbench →</button></div>`;
  $('#mp-workbench').addEventListener('click', () => { location.href = `/legacy#/mapping?case=${id}`; });
}

/* ── step 3: standardize (reuses views-analytics global) ── */
async function renderStandardize(panel, id) {
  panel.innerHTML = `<div id="v3-std"></div>`;
  const host = $('#v3-std');
  const d = await api(`/api/v1/cases/${id}/standardize`);
  const metrics = [['Standardized Lives', d.memberCount ?? d.lives], ['Claim Records', d.claimCount], ['Data Quality Score', d.dqScore != null ? Math.round(d.dqScore * 100) + '%' : '—']];
  panel.innerHTML = `
  <h1 class="ox-display" style="font-size:24px">3. Data Standardization & Quality</h1>
  <div class="v3-grid" style="margin-top:12px">
    ${metrics.map(([l, v]) => `<div class="v3-card v3-c4"><div class="v3-small">${l}</div><div class="v3-metric">${esc(v ?? '—')}</div></div>`).join('')}
    <div class="v3-card v3-c12"><h3 class="ox-display">Validation & Preview</h3>
      <div class="ox-table-wrap"><table class="ox-table">
        <thead><tr><th>Claim</th><th>Member</th><th>Status</th><th>Paid ₹</th><th>Diagnosis</th></tr></thead>
        <tbody>${(d.preview || []).slice(0, 12).map(r => `
          <tr><td class="ox-mono">${esc(r.claimNumber)}</td><td>${esc(r.memberNumber || '—')}</td>
          <td>${esc(r.claimStatus)}</td><td class="ox-num">${inr(r.paidAmount)}</td>
          <td class="v3-small">${esc((r.diagnosisText || '').slice(0, 40))}</td></tr>`).join('') || '<tr><td colspan="5" class="v3-small">Run the pipeline first.</td></tr>'}</tbody>
      </table></div>
    </div>
  </div>`;
}

/* ── step 4: disease mapping (coding) ── */
async function renderCoding(panel, id) {
  panel.innerHTML = '<div class="ox-skeleton" style="height:160px"></div>';
  const d = await api(`/api/v1/cases/${id}/coding`);
  const rows = d.breakdowns || d.rows || d.summary || [];
  panel.innerHTML = `
  <h1 class="ox-display" style="font-size:24px">4. Disease & Department Mapping</h1>
  <p class="v3-small">Taxonomy classification of diagnosis text into departments and chronic/critical categories.</p>
  <div class="v3-grid">
    <div class="v3-card v3-c4"><div class="v3-small">Taxonomy Mapped</div><div class="v3-metric">${d.mappedPct != null ? Math.round(d.mappedPct) + '%' : '—'}</div></div>
    <div class="v3-card v3-c4"><div class="v3-small">Chronic Claims</div><div class="v3-metric">${d.chronicCount ?? '—'}</div></div>
    <div class="v3-card v3-c4"><div class="v3-small">Critical Claims</div><div class="v3-metric">${d.criticalCount ?? '—'}</div></div>
    <div class="v3-card v3-c12"><h3 class="ox-display">Mapped Disease Summary</h3>
      <div class="ox-table-wrap"><table class="ox-table">
        <thead><tr><th>Category</th><th>Claims</th><th>Paid ₹</th></tr></thead>
        <tbody>${(Array.isArray(rows) ? rows : []).slice(0, 15).map(r => `
          <tr><td>${esc(r.category || r.disease || r.department || '—')}</td>
          <td class="ox-num">${r.claims ?? r.count ?? '—'}</td><td class="ox-num">${inr(r.paidAmount ?? r.paid)}</td></tr>`).join('')
          || '<tr><td colspan="3" class="v3-small">Run the pipeline first.</td></tr>'}</tbody>
      </table></div>
    </div>
  </div>`;
}

/* ── step 5: analytics (delegates to the global from views-analytics.js) ── */
async function renderAnalytics(panel, id) {
  panel.innerHTML = `
  <h1 class="ox-display" style="font-size:24px">5. Underwriting Analytics & Credibility Engine</h1>
  <div id="v3-ana-host"></div>`;
  const host = $('#v3-ana-host');
  host.innerHTML = '<div class="ox-skeleton" style="height:200px"></div>';
  try {
    const [d, my] = await Promise.all([
      api(`/api/v1/cases/${id}/analytics`),
      api(`/api/v1/cases/${id}/multiyear`).catch(() => null),
    ]);
    if (!d.computed) { host.innerHTML = `<div class="ox-banner ox-banner--warn">${esc(d.message || 'Run the pipeline first.')}</div>`; return; }
    const a = d.snapshot;
    host.innerHTML = `
    <div class="v3-grid" style="margin-bottom:18px">
      <div class="v3-card v3-c3"><div class="v3-small">Covered Lives</div><div class="v3-metric">${a.coveredLives ?? '—'}</div></div>
      <div class="v3-card v3-c3"><div class="v3-small">Claims</div><div class="v3-metric">${a.claimCount ?? '—'}</div></div>
      <div class="v3-card v3-c3"><div class="v3-small">Paid Claims</div><div class="v3-metric">${a.paidClaimCount ?? '—'}</div></div>
      <div class="v3-card v3-c3"><div class="v3-small">Burn / Life</div><div class="v3-metric">${inr(a.burnCostPerLife)}</div></div>
    </div>
    ${my?.years?.length ? `
    <div class="v3-card" style="margin-bottom:18px">
      <h3 class="ox-display">3-Year Company Experience</h3>
      <div class="ox-table-wrap"><table class="ox-table">
        <thead><tr><th>Policy Year</th><th>Lives</th><th>Paid Claims</th><th>Paid ₹</th><th>Frequency</th><th>Burn / Life</th></tr></thead>
        <tbody>${my.years.map(y => `
          <tr><td><b>${esc(y.policyYear)}</b></td><td class="ox-num">${y.lives}</td><td class="ox-num">${y.paidClaims}</td>
          <td class="ox-num">${inr(y.paidAmount)}</td><td class="ox-num">${y.frequencyPct}%</td><td class="ox-num">${inr(y.burnCostPerLife)}</td></tr>`).join('')}</tbody>
      </table></div>
    </div>` : ''}
    <div class="v3-card"><h3 class="ox-display">Age-Band / Demography Drivers</h3>
      <div class="ox-table-wrap"><table class="ox-table">
        <thead><tr><th>Band</th><th>Lives</th><th>Claims</th><th>Paid ₹</th><th>Chronic %</th></tr></thead>
        <tbody>${(a.ageBands || []).map(b => `
          <tr><td>${esc(b.band)}</td><td class="ox-num">${b.lives ?? '—'}</td><td class="ox-num">${b.claims ?? '—'}</td>
          <td class="ox-num">${inr(b.paidAmount)}</td><td class="ox-num">${b.chronicPct != null ? Math.round(b.chronicPct) + '%' : '—'}</td></tr>`).join('') || '<tr><td colspan="5" class="v3-small">—</td></tr>'}</tbody>
      </table></div>
    </div>`;
  } catch (ex) { host.innerHTML = errBanner(ex); }
}

/* ── step 6: guardrails ── */
async function renderGuardrails(panel, id) {
  panel.innerHTML = '<div class="ox-skeleton" style="height:160px"></div>';
  let d;
  try { d = await api(`/api/v1/cases/${id}/guardrails`); } catch (ex) { panel.innerHTML = errBanner(ex); return; }
  const evals = d.evaluations || d.result?.evaluations || [];
  const conf = d.confidence ?? d.result?.confidence;
  const outcome = d.outcome || d.result?.outcome || '—';
  panel.innerHTML = `
  <h1 class="ox-display" style="font-size:24px">6. Guardrails & Automated Risk Assessment</h1>
  <div class="v3-grid">
    <div class="v3-card v3-c4">
      <div class="v3-small">Quote Confidence</div><div class="v3-metric">${conf != null ? Math.round(conf * 100) + '%' : '—'}</div>
      <div class="ox-bar" style="margin-top:6px"><span style="width:${conf != null ? Math.round(conf * 100) : 0}%"></span></div>
      <p class="v3-small" style="margin-top:10px">Outcome: <b>${esc(outcome)}</b> · ${evals.filter(e => e.passed).length}/${evals.length} rules passed</p>
    </div>
    <div class="v3-card v3-c8"><h3 class="ox-display">Guardrail Results</h3>
      <div class="ox-table-wrap"><table class="ox-table">
        <thead><tr><th>Rule</th><th>Threshold</th><th>Observed</th><th>Severity</th><th>Result</th></tr></thead>
        <tbody>${evals.map(e => `
          <tr><td>${esc(e.message || e.ruleCode)}</td>
          <td class="ox-num">${esc(e.operator || '')} ${esc(e.threshold)}</td><td class="ox-num">${esc(e.observed)}</td>
          <td><span class="ox-badge ${e.severity === 'HardStop' ? 'ox-badge--danger' : e.severity === 'Refer' ? 'ox-badge--warn' : 'ox-badge--neutral'}">${esc(e.severity)}</span></td>
          <td>${e.passed ? '✓' : '✗'}</td></tr>`).join('') || '<tr><td colspan="5" class="v3-small">Run the pipeline first.</td></tr>'}</tbody>
      </table></div>
    </div>
  </div>
  <div class="ox-row" style="margin-top:14px"><button class="ox-btn ox-btn--ghost" id="gr-admin">Configure rules →</button></div>`;
  $('#gr-admin').addEventListener('click', renderAdminConsole);
}

/* ── step 7: pricing ── */
async function renderPricing(panel, id) {
  panel.innerHTML = '<div class="ox-skeleton" style="height:160px"></div>';
  let qv;
  try {
    const quotes = await api(`/api/v1/cases/${id}/quotes`);
    qv = quotes.find(q => q.status === 'Current') || quotes[quotes.length - 1];
  } catch (ex) { panel.innerHTML = errBanner(ex); return; }
  const p = qv?.pricing || {};
  const bridge = [
    ['Observed Burn / Life', p.observedBurnPerLife],
    ['Medical Trend', p.trendPct != null ? pct(p.trendPct) : null],
    ['IBNR', p.ibnrPct != null ? pct(p.ibnrPct) : null],
    ['Projected Burn / Life', p.projectedBurnPerLife],
    ['Risk Margin', p.riskMarginPerLife],
    ['Expense Loading', p.expensePerLife],
    ['Technical Premium / Life', p.technicalPremiumPerLife],
  ];
  panel.innerHTML = `
  <h1 class="ox-display" style="font-size:24px">7. Rule Pricing & UW Intervention</h1>
  <div class="v3-grid">
    <div class="v3-card v3-c4"><div class="v3-small">Observed Burn / Life</div><div class="v3-metric">${inr(p.observedBurnPerLife)}</div></div>
    <div class="v3-card v3-c4"><div class="v3-small">Projected Burn / Life</div><div class="v3-metric">${inr(p.projectedBurnPerLife)}</div></div>
    <div class="v3-card v3-c4"><div class="v3-small">Technical Premium</div><div class="v3-metric">${inr(qv?.grossPremium ?? p.technicalPremium)}</div></div>
    <div class="v3-card v3-c12"><h3 class="ox-display">Pricing Bridge</h3>
      <div class="ox-table-wrap"><table class="ox-table">
        <thead><tr><th>Component</th><th>Value</th></tr></thead>
        <tbody>${bridge.map(([l, v]) => `<tr><td>${l}</td><td class="ox-num">${esc(v ?? '—')}</td></tr>`).join('')}</tbody>
      </table></div>
    </div>
  </div>
  <div class="ox-row" style="margin-top:14px">
    <button class="ox-btn ox-btn--ghost" id="pr-override">Override workbench →</button>
    <button class="ox-btn ox-btn--ghost" id="pr-formula">Trend / IBNR formula →</button>
  </div>`;
  $('#pr-override').addEventListener('click', () => { location.href = `/legacy#/case/${id}`; });
  $('#pr-formula').addEventListener('click', renderAdminConsole);
}

/* ── step 8: benefits ── */
async function renderBenefits(panel, id) {
  panel.innerHTML = '<div class="ox-skeleton" style="height:160px"></div>';
  let d;
  try { d = await api(`/api/v1/cases/${id}/benefits`); } catch (ex) { panel.innerHTML = errBanner(ex); return; }
  const lines = d.lines || [];
  panel.innerHTML = `
  <h1 class="ox-display" style="font-size:24px">8. Expiring vs Proposed Benefits</h1>
  <div class="v3-grid" style="margin-bottom:18px">
    <div class="v3-card v3-c12"><div class="v3-live-premium">
      <div class="ox-row" style="justify-content:space-between">
        <div><div class="v3-small">LIVE TECH PREMIUM</div><div class="v3-live-amount">${inr(d.liveTechnicalPremium ?? d.technicalPremium)}</div></div>
        <div style="text-align:right"><div class="v3-small">RATE / LIFE</div><div style="font-size:20px;font-weight:700">${inr(d.liveTechnicalRate ?? d.ratePerLife)}</div></div>
      </div>
    </div></div>
  </div>
  <div class="v3-card"><h3 class="ox-display">Core Benefit Comparison</h3>
    <div class="ox-table-wrap"><table class="ox-table">
      <thead><tr><th>Line</th><th>Expiring</th><th>Proposed</th><th>Change</th><th>Pricing Impact</th></tr></thead>
      <tbody>${lines.map(l => `
        <tr><td><b>${esc(l.lineName || l.name)}</b></td>
        <td class="ox-num">${esc(l.expiring ?? '—')}</td><td class="ox-num">${esc(l.proposed ?? '—')}</td>
        <td>${esc(l.change ?? '—')}</td><td class="ox-num">${inr(l.pricingImpact ?? l.impactPm)}</td></tr>`).join('') || '<tr><td colspan="5" class="v3-small">Run the pipeline first.</td></tr>'}</tbody>
    </table></div>
  </div>`;
}

/* ── step 9: final quote ── */
async function renderFinalQuote(panel, id) {
  panel.innerHTML = '<div class="ox-skeleton" style="height:160px"></div>';
  let d;
  try { d = await api(`/api/v1/cases/${id}/final-quote`); } catch (ex) { panel.innerHTML = errBanner(ex); return; }
  const q = d.quote || d;
  panel.innerHTML = `
  <h1 class="ox-display" style="font-size:24px">9. Final Quotation & Proposal</h1>
  <div class="v3-grid">
    <div class="v3-card v3-c12"><h3 class="ox-display">Sales Commission & Commercials</h3>
      <div class="v3-grid">
        <div class="v3-c4"><div class="v3-small">Premium Before Commission</div><div class="v3-metric">${inr(q.netPremium ?? q.grossPremium)}</div></div>
        <div class="v3-c4"><div class="v3-small">Gross (pre-GST)</div><div class="v3-metric">${inr(q.grossPremium)}</div></div>
        <div class="v3-c4"><div class="v3-small">Final (incl. GST)</div><div class="v3-metric">${inr(q.grossPremium ? q.grossPremium * 1.18 : null)}</div></div>
      </div>
    </div>
    <div class="v3-card v3-c12"><h3 class="ox-display">Quotation — ${esc(CASE.clientName)}</h3>
      <div class="ox-table-wrap"><table class="ox-table">
        <thead><tr><th>Component</th><th>Value</th></tr></thead>
        <tbody>
          <tr><td>Covered Lives</td><td class="ox-num">${q.analytics?.coveredLives ?? '—'}</td></tr>
          <tr><td>SI Structure</td><td>${esc(q.siStructure || 'Tiered matrix')}</td></tr>
          <tr><td>Net Premium</td><td class="ox-num">${inr(q.netPremium)}</td></tr>
          <tr><td>Gross Premium (pre-GST)</td><td class="ox-num">${inr(q.grossPremium)}</td></tr>
          <tr><td>GST 18%</td><td class="ox-num">${inr(q.grossPremium ? q.grossPremium * 0.18 : null)}</td></tr>
          <tr><td><b>Final Annual Premium</b></td><td class="ox-num"><b>${inr(q.grossPremium ? q.grossPremium * 1.18 : null)}</b></td></tr>
        </tbody>
      </table></div>
      <div class="ox-row" style="margin-top:14px;justify-content:space-between">
        <button class="ox-btn ox-btn--ghost" id="fq-discount">Request discount →</button>
        <div class="ox-row">
          <button class="ox-btn ox-btn--ghost" onclick="window.print()">Print / PDF</button>
          <button class="ox-btn ox-btn--primary" id="fq-dl">Download JSON</button>
        </div>
      </div>
    </div>
  </div>`;
  $('#fq-dl').addEventListener('click', () => {
    const blob = new Blob([JSON.stringify(d, null, 2)], { type: 'application/json' });
    const a = document.createElement('a'); a.href = URL.createObjectURL(blob);
    a.download = `${CASE.caseNumber}-quote.json`; a.click();
  });
  $('#fq-discount').addEventListener('click', () => { location.href = `/legacy#/case/${id}`; });
}

/* ── UW referral workbench ── */
async function openWorkbench(caseId) {
  root().innerHTML = '<div class="ox-skeleton" style="height:220px"></div>';
  let c;
  try { c = await api(`/api/v1/cases/${caseId}`); } catch (ex) { root().innerHTML = errBanner(ex); return; }
  CASE = c;
  $('#v3-case-pill').textContent = c.caseNumber;
  const gr = c.quoteVersions?.find(q => q.status === 'Current')?.guardrails
             || c.guardrails;
  const failed = gr?.evaluations?.filter(e => !e.passed) || [];
  root().innerHTML = `
  <div class="ox-row" style="justify-content:space-between;align-items:flex-start;margin-bottom:18px">
    <div>
      <div class="v3-small">Underwriting Workbench</div>
      <h1 class="ox-display" style="font-size:24px;margin:2px 0">${esc(c.caseNumber)} — ${esc(c.clientName)}</h1>
      <p class="v3-small" style="margin:0">${failed.length ? 'Referral: ' + esc(failed.map(e => e.message).join('; ')) : 'Review & decide'}</p>
    </div>
    <button class="ox-btn ox-btn--ghost" id="wb-back">← Back to Queue</button>
  </div>
  <div class="v3-grid" style="margin-bottom:18px">
    <div class="v3-card v3-c3"><div class="v3-small">Status</div><b>${esc(c.status)}</b></div>
    <div class="v3-card v3-c3"><div class="v3-small">Stage</div><b>${esc(c.currentStage || '—')}</b></div>
    <div class="v3-card v3-c3"><div class="v3-small">Lives</div><b>${c.quoteVersions?.[0]?.analytics?.coveredLives ?? '—'}</b></div>
    <div class="v3-card v3-c3"><div class="v3-small">Premium</div><b>${inr(c.quoteVersions?.[0]?.grossPremium)}</b></div>
  </div>
  <div class="v3-card"><h2 class="ox-display">Underwriter Action</h2>
    <div class="v3-grid" style="margin-top:14px">
      <div class="v3-card v3-c6" id="wb-approve" style="cursor:pointer"><h3 class="ox-display">✓ Approve Quote</h3>
        <p class="v3-small">Accept standard pricing or approved overrides. Issues final approval back to Sales.</p></div>
      <div class="v3-card v3-c6" id="wb-modify" style="cursor:pointer"><h3 class="ox-display">⚙ Modify Pricing Rules</h3>
        <p class="v3-small">Adjust trend, IBNR, credibility, or risk margin in the pricing engine workbench.</p></div>
    </div>
    <div id="wb-approve-pane" class="ox-hidden" style="margin-top:16px">
      <label class="ox-mono-label">Approval Remarks *</label>
      <textarea class="ox-input" id="wb-remarks" rows="3" placeholder="Enter formal underwriting rationale..."></textarea>
      <div class="v3-footerbar"><button class="ox-btn ox-btn--primary" id="wb-confirm">Confirm Approval & Send to Sales</button></div>
    </div>
  </div>`;
  $('#wb-back').addEventListener('click', renderUWQueue);
  $('#wb-approve').addEventListener('click', () => $('#wb-approve-pane').classList.toggle('ox-hidden'));
  $('#wb-modify').addEventListener('click', () => openStepper(caseId) || setTimeout(() => showStep(7), 50));
  $('#wb-confirm').addEventListener('click', async () => {
    const remarks = $('#wb-remarks').value.trim();
    if (!remarks) return alert('Remarks required');
    try {
      await post('/api/v1/uw/decisions', { caseId, action: 'Approve', remarks });
      await renderUWQueue();
    } catch (ex) { alert('Decision failed: ' + ex.message); }
  });
}

/* ── admin formula console ── */
async function renderAdminConsole() {
  root().innerHTML = '<div class="ox-skeleton" style="height:180px"></div>';
  $('#v3-case-pill').textContent = 'Master Config';
  let f = { TrendPct: 8, IbnrPct: 5 };
  try { f = await api('/api/v1/admin/formulas'); } catch { /* defaults */ }
  root().innerHTML = `
  <div class="ox-row" style="justify-content:space-between;margin-bottom:20px">
    <div><h1 class="ox-display" style="font-size:26px;margin:0">Opexminds Master Governance Console</h1>
    <p class="v3-small" style="margin:4px 0 0">Govern actuarial formulas, pricing masters, and risk threshold parameters.</p></div>
    <button class="ox-btn ox-btn--ghost" id="adm-back">← Back to Queue</button>
  </div>
  <div class="v3-grid">
    <div class="v3-card v3-c6"><h3 class="ox-display">Medical Trend & IBNR Formula</h3>
      <p class="v3-small">Core actuarial assumptions applied during burn calculation on the next pipeline run.</p>
      <div class="v3-grid" style="margin-top:12px">
        <div class="v3-c6"><label class="ox-mono-label">Base Trend %</label><input class="ox-input" id="fm-trend" type="number" step="0.25" value="${f.TrendPct ?? 8}"/></div>
        <div class="v3-c6"><label class="ox-mono-label">Default IBNR %</label><input class="ox-input" id="fm-ibnr" type="number" step="0.25" value="${f.IbnrPct ?? 5}"/></div>
      </div>
      <button class="ox-btn ox-btn--primary" id="fm-save" style="margin-top:14px">Save Formula Config</button>
      <div class="v3-small" id="fm-status" style="margin-top:8px"></div>
    </div>
    <div class="v3-card v3-c6"><h3 class="ox-display">Guardrail Rules & Masters</h3>
      <p class="v3-small">20-rule engine, editability, and Excel masters upload live in the governance console.</p>
      <div class="ox-row" style="margin-top:14px">
        <button class="ox-btn ox-btn--ghost" id="adm-rules">Rule configurator →</button>
        <button class="ox-btn ox-btn--ghost" id="adm-masters">Masters upload →</button>
      </div>
    </div>
  </div>`;
  $('#adm-back').addEventListener('click', renderUWQueue);
  $('#adm-rules').addEventListener('click', () => { location.href = '/legacy#/admin'; });
  $('#adm-masters').addEventListener('click', () => { location.href = '/legacy#/admin'; });
  $('#fm-save').addEventListener('click', async () => {
    try {
      await post('/api/v1/admin/formulas', { TrendPct: Number($('#fm-trend').value), IbnrPct: Number($('#fm-ibnr').value) });
      $('#fm-status').textContent = '✓ Saved — applies on the next pipeline run';
    } catch (ex) { $('#fm-status').textContent = 'Failed: ' + ex.message; }
  });
}

/* ── theme + chrome ── */
const html = document.documentElement;
$('#v3-theme-btn').addEventListener('click', () => {
  const light = html.getAttribute('data-theme') === 'light';
  html.setAttribute('data-theme', light ? 'dark' : 'light');
  $('#v3-theme-btn').textContent = light ? '☀ THEME' : '🌙 THEME';
});
$('#v3-logout-btn').addEventListener('click', async () => { await post('/api/v1/auth/logout'); ME = null; renderLogin(); });
$('#v3-queue-btn').addEventListener('click', () => { if (ME) enter(); });

/* ── boot ── */
(async () => {
  try { ME = await api('/api/v1/auth/me'); enter(); }
  catch { renderLogin(); }
})();
})();
