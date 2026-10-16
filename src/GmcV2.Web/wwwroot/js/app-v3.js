/* GMC v3 — role-first shell (Dhrumil's approved prototype UX) wired to the live engine.
   Loads AFTER views-analytics.js. app.js is NOT loaded here — v3 is self-contained.

   Upgrade (prototype delta):
   · Admin Console — 4 tabs: Pricing Formula Configurator / Guardrail Configurator /
     Excel Masters / Rule Classification (/admin/formulas, /admin/guardrail-rules, /admin/masters/upload).
   · P5 Analytics — 5 sub-tabs: All / 01 3-Year & SI Experience / 02 Disease & Demography
     Risk / 03 Chronic SI Scenario Lab / 04 Cell Credibility Engine.
   · P8 Benefits — 5 sub-tabs: All / 01 Core Benefits & SI Bands / 02 Disease Sub-Limits /
     03 Incremental Impact Bridge / 04 Quote Versions.
   · UW Referral Workbench — 3 decisions: Approve (remarks+conditions+authority),
     Manual Quote (premium+decision+Excel/PDF attach), Return (decision+remarks+due date).
   · Sales case drawer — quote VERSIONS with select-for-UW radio + discount request with
     live premium/GST preview → /discounts (UW decides in the workbench).
*/
(() => {
'use strict';
const $ = (s) => document.querySelector(s);
const esc = (v) => String(v ?? '').replace(/[&<>"']/g, (c) => ({'&': '&' + 'amp;', '<': '&' + 'lt;', '>': '&' + 'gt;', '"': '&' + 'quot;', "'": '&' + '#39;'})[c]);
const inr = (v) => v == null || v === '' ? '—' : '₹' + Number(v).toLocaleString('en-IN', { maximumFractionDigits: 2 });
const pct = (v) => v == null || v === '' ? '—' : (Number(v)).toFixed(1) + '%';

const api = async (path, opts = {}) => {
  const r = await fetch(path, { headers: { 'Content-Type': 'application/json' }, credentials: 'same-origin', ...opts });
  if (!r.ok) {
    let msg = '', body = null;
    try { const j = await r.json(); if (j.error) msg = j.error; body = j; } catch { /* no body */ }
    if (!msg) msg = `HTTP ${r.status}${r.status === 403 ? ' — admin role required' : ''}`;
    const ex = new Error(msg); ex.body = body; ex.status = r.status; throw ex;
  }
  const ct = r.headers.get('content-type') || '';
  return ct.includes('json') ? r.json() : r.text();
};
const post = (path, body) => api(path, { method: 'POST', body: JSON.stringify(body ?? {}) });
const uploadFile = async (path, file, docType) => {
const fd = new FormData(); fd.append('file', file);
if (docType) fd.append('docType', docType);
const r = await fetch(path, { method: 'POST', body: fd, credentials: 'same-origin' });
if (!r.ok) { let msg = ''; try { const j = await r.json(); if (j.error) msg = j.error; } catch { /* */ } if (!msg) msg = `HTTP ${r.status}`; throw new Error(msg); }
return r.json();
};

let ME = null;
let CASE = null;          // active case context for the stepper
let QUEUE = [];           // /api/v1/queue rows
let UW_FILTER = 'all', UW_SEARCH = '';
let SQ_FILTER = 'all', SQ_SEARCH = '';
let P5_TAB = 'all', P8_TAB = 'all';
let ADMIN_TAB = 'formula';
const canDecideRole = () => ['Underwriter', 'SeniorUnderwriter', 'ChiefUnderwriter', 'Admin'].includes(ME?.role);
const isAdminRole = () => ME?.role === 'Admin';
const savedVersion = (caseId) => Number(localStorage.getItem('gmc_uw_sel_' + caseId) || 0) || null;
const setSavedVersion = (caseId, qvId) => { try { localStorage.setItem('gmc_uw_sel_' + caseId, String(qvId)); } catch { /* private mode */ } };

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
  const err = $('#v3-login-err'); err.classList.add('ox-hide');
  try {
    await post('/api/v1/auth/login', { email, password });
    ME = await api('/api/v1/auth/me');
    enter();
  } catch (ex) {
    err.textContent = ex.message; err.classList.remove('ox-hide');
  }
}

/* ── entry after auth ── */
function enter() {
  $('#v3-role-pill').textContent = ROLE_LABEL[ME.role] || ME.role;
  $('#v3-logout-btn').style.display = '';
  $('#v3-queue-btn').style.display = '';
  $('#v3-queue-btn').textContent = (ME.role === 'SalesPerson' || ME.role === 'SalesAgent') ? 'Sales Queue' : 'UW Queue';
  if (ME.role === 'SalesPerson' || ME.role === 'SalesAgent') renderSalesQueue();
  else renderUWQueue();
}

/* ── screen: UW queue ── */
async function renderUWQueue() {
  root().innerHTML = '<div class="ox-skeleton" style="height:220px"></div>';
  $('#v3-case-pill').textContent = 'UW Governance';
  try { QUEUE = await api('/api/v1/queue'); } catch (ex) { root().innerHTML = errBanner(ex); return; }
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
      ${isAdminRole() ? '<button class="ox-btn ox-btn--ghost" id="v3-admin-btn">⚙ Admin Console</button>' : ''}
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
        ${[['all', 'All'], ['quote', 'Quoted'], ['referral', 'Referral / UW'], ['approved', 'Approved'], ['returned', 'Returned']]
          .map(([k, l]) => `<button class="v3-queue-tab ${SQ_FILTER === k ? 'active' : ''}" data-f="${k}">${l}</button>`).join('')}
      </div>
      <input class="ox-input" id="v3-sq-search" placeholder="Search client..." style="max-width:260px" value="${esc(SQ_SEARCH)}"/>
    </div>
    <div class="ox-table-wrap"><table class="ox-table">
      <thead><tr><th>Case ID</th><th>Client</th><th>Lives</th><th>Premium</th><th>Quote Status</th><th>UW Status</th><th>Latest Remark</th><th>Updated</th><th></th></tr></thead>
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

function sqQuoteStatus(q) {
  const s = String(q.status || '');
  if (s === 'Approved') return '<span class="ox-badge ox-badge--ok">Approved</span>';
  if (s === 'ApprovedWithChanges') return '<span class="ox-badge ox-badge--ok">Approved w/ Changes</span>';
  if (q.premium != null) return '<span class="ox-badge ox-badge--ok">Quote Generated</span>';
  return '<span class="ox-badge ox-badge--neutral">Draft</span>';
}
function sqUWStatus(q) {
  const s = String(q.status || '');
  if (q.isReferral) return '<span class="ox-badge ox-badge--warn">Under Review</span>';
  if (s === 'Approved' || s === 'ApprovedWithChanges') return '<span class="ox-badge ox-badge--ok">Approved</span>';
  if (s === 'Declined' || s === 'Rejected') return '<span class="ox-badge ox-badge--danger">Declined</span>';
  if (s === 'ReturnedForClarification' || s === 'NeedMoreInfo') return '<span class="ox-badge ox-badge--warn">Returned</span>';
  return '<span class="ox-badge ox-badge--neutral">—</span>';
}

function renderSQRows() {
  const body = $('#v3-sq-body'); if (!body) return;
  let rows = QUEUE;
  if (SQ_FILTER === 'quote') rows = rows.filter(q => q.premium != null);
  else if (SQ_FILTER === 'referral') rows = rows.filter(q => q.isReferral);
  else if (SQ_FILTER === 'approved') rows = rows.filter(q => q.status === 'Approved' || q.status === 'ApprovedWithChanges');
  else if (SQ_FILTER === 'returned') rows = rows.filter(q => q.status === 'ReturnedForClarification' || q.status === 'NeedMoreInfo' || q.status === 'Declined');
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
      <td>${sqQuoteStatus(q)}</td>
      <td>${sqUWStatus(q)}</td>
      <td class="v3-small" style="max-width:240px;white-space:normal">${esc(String(q.reason || '—').slice(0, 120))}</td>
      <td class="v3-small">${(q.updatedAt || '').slice(0, 10)}</td>
      <td class="ox-right"><button class="ox-btn ox-btn--ghost ox-btn--sm" data-drawer="${q.caseId}">Open</button></td>
    </tr>`).join('') || '<tr><td colspan="9" class="v3-small">No cases match.</td></tr>';
  body.querySelectorAll('[data-drawer]').forEach(b => b.addEventListener('click', () => openSalesDrawer(Number(b.dataset.drawer))));
}

const errBanner = (ex) => `<div class="ox-banner ox-banner--error">Failed to load: ${esc(ex.message)}</div>`;


/* ── screen: sales case drawer (quote versions + select-for-UW + discount request) ── */
async function openSalesDrawer(caseId) {
  root().innerHTML = '<div class="ox-skeleton" style="height:220px"></div>';
  let c, quotes = [], discounts = [], benefits = null;
  try {
    [c, quotes, discounts] = await Promise.all([
      api(`/api/v1/cases/${caseId}`),
      api(`/api/v1/cases/${caseId}/quotes`).catch(() => []),
      api(`/api/v1/cases/${caseId}/discounts`).catch(() => []),
    ]);
    benefits = await api(`/api/v1/cases/${caseId}/benefits`).catch(() => null);
  } catch (ex) { root().innerHTML = errBanner(ex); return; }
  CASE = c;
  $('#v3-case-pill').textContent = c.caseNumber;
  const current = quotes.find(q => q.status === 'Current') || quotes[quotes.length - 1];
  const basePremium = current?.grossPremium ?? 0;
  const gr = current?.guardrails;
  const failed = gr?.evaluations?.filter(e => !e.passed) || [];

  const versionRow = (q) => {
    const sel = savedVersion(caseId) === q.quoteVersionId;
    return `<div class="v3-version-item ${sel ? 'selected' : ''}" data-vrow="${q.quoteVersionId}">
      <div class="v3-version-top">
        <div>
          <b>Version ${q.versionNo}</b> <span class="ox-badge ${q.status === 'Current' ? 'ox-badge--ok' : 'ox-badge--neutral'}">${esc(q.status)}</span>
          ${q.revisionReason ? `<div class="v3-small" style="margin-top:4px">Reason: ${esc(q.revisionReason)}</div>` : ''}
          <div class="v3-small" style="margin-top:4px">${esc((q.createdAtUtc || '').slice(0, 16).replace('T', ' '))} · by ${esc(q.createdBy || 'Pipeline')}</div>
        </div>
        <div class="ox-row" style="gap:12px">
          <b class="ox-num">${inr(q.grossPremium)}</b>
          <label class="v3-small" style="display:flex;align-items:center;gap:6px;cursor:pointer"><input type="radio" name="v3-sel-uw" value="${q.quoteVersionId}" ${sel ? 'checked' : ''}/> Select for UW</label>
        </div>
      </div>
    </div>`;
  };

  root().innerHTML = `
  <div class="ox-row" style="justify-content:space-between;align-items:flex-start;margin-bottom:18px">
    <div>
      <div class="v3-small">Sales Case</div>
      <h1 class="ox-display" style="font-size:24px;margin:2px 0">${esc(c.caseNumber)} — ${esc(c.clientName)}</h1>
      <p class="v3-small" style="margin:0">${esc(c.businessType || '')} · ${esc(c.status)} · stage ${esc(c.currentStage || '—')}</p>
    </div>
    <button class="ox-btn ox-btn--ghost" id="sd-back">← Back to Queue</button>
  </div>
  ${failed.length ? `<div class="ox-banner ox-banner--warn" style="margin-bottom:16px"><b>Referred to UW:</b> ${esc(failed.map(e => e.message).join(' · '))}</div>` : ''}
  <div class="v3-grid" style="margin-bottom:16px">
    <div class="v3-card v3-c3"><div class="v3-small">Lives</div><div class="v3-metric">${current?.coveredLives || '—'}</div></div>
    <div class="v3-card v3-c3"><div class="v3-small">Current Premium</div><div class="v3-metric">${inr(basePremium)}</div></div>
    <div class="v3-card v3-c3"><div class="v3-small">Quote Status</div><div style="margin-top:10px">${sqQuoteStatus({ status: c.status, premium: basePremium })}</div></div>
    <div class="v3-card v3-c3"><div class="v3-small">UW Status</div><div style="margin-top:10px">${sqUWStatus({ status: c.status, isReferral: c.status === 'ReferredToUW' })}</div></div>
  </div>
  <div class="v3-grid">
    <div class="v3-card v3-c7">
      <h2 class="ox-display">Quote Versions</h2>
      <p class="v3-small">Create multiple cover combinations under the same Case ID. Only one version can be marked for Underwriting.</p>
      <div id="sd-versions">${quotes.map(versionRow).join('') || '<p class="v3-small">No quote versions yet — run the pipeline first.</p>'}</div>
    </div>
    <div class="v3-card v3-c5">
      <h2 class="ox-display">Request Commercial Discount</h2>
      <p class="v3-small">Request a premium discount only. Benefits and cover conditions remain unchanged.</p>
      <label class="ox-mono-label" style="margin-top:10px">REQUESTED DISCOUNT % *</label>
      <input class="ox-input" id="sd-pct" type="number" min="0.25" max="50" step="0.25" value="5"/>
      <div class="v3-grid" style="margin-top:12px;gap:8px">
        <div class="v3-c6"><div class="v3-small">Current final premium</div><b class="ox-num" id="sd-base">${inr(basePremium)}</b></div>
        <div class="v3-c6"><div class="v3-small">Premium after discount</div><b class="ox-num" id="sd-after">—</b></div>
        <div class="v3-c6"><div class="v3-small">GST @18%</div><b class="ox-num" id="sd-gst">—</b></div>
        <div class="v3-c6"><div class="v3-small">Total incl. GST</div><b class="ox-num" id="sd-total">—</b></div>
      </div>
      <label class="ox-mono-label" style="margin-top:12px">SALES JUSTIFICATION / REMARKS *</label>
      <textarea class="ox-input" id="sd-remarks" rows="3" placeholder="Strategic account; requesting 5% commercial discount. No change in covers or conditions."></textarea>
      <div class="ox-row" style="margin-top:12px;justify-content:flex-end">
        <button class="ox-btn ox-btn--primary" id="sd-send">Send Discount Request to UW</button>
      </div>
      <div class="v3-small" id="sd-status" style="margin-top:8px"></div>
      ${discounts.length ? `<div style="margin-top:14px"><h3 class="ox-display">Discount Requests</h3>
        ${discounts.map(d => `<div class="v3-version-item"><div class="v3-version-top">
          <div><b>${pct(d.requestedPct)}</b> <span class="ox-badge ${d.status === 'Approved' ? 'ox-badge--ok' : d.status === 'Declined' ? 'ox-badge--danger' : 'ox-badge--warn'}">${esc(d.status)}</span>
          <div class="v3-small" style="margin-top:4px">by ${esc(d.requestedByEmail || '—')} · ${esc(String(d.requestedRemarks || '').slice(0, 90))}</div>
          ${d.approvedPct != null ? `<div class="v3-small">Approved at ${pct(d.approvedPct)} — ${esc(d.approvalRemarks || '')}</div>` : ''}</div>
        </div></div>`).join('')}</div>` : ''}
    </div>
    ${benefits?.competitors?.length ? `<div class="v3-card v3-c12"><h2 class="ox-display">Competition Premium Summary</h2>
      <p class="v3-small">Reference only. Competitor premium does not alter technical pricing.</p>
      <div class="ox-table-wrap"><table class="ox-table">
        <thead><tr><th>Competitor</th><th>Sum Insured</th><th>Premium</th><th>Δ vs Ours</th></tr></thead>
        <tbody>${benefits.competitors.map(k => `<tr><td><b>${esc(k.name)}</b></td><td class="ox-num">${inr(k.sumInsured)}</td>
          <td class="ox-num">${inr(k.premium)}</td><td class="ox-num">${k.deltaPct != null ? pct(k.deltaPct) : '—'}</td></tr>`).join('')}</tbody>
      </table></div></div>` : ''}
  </div>`;
  $('#sd-back').addEventListener('click', renderSalesQueue);
  // live discount preview
  const preview = () => {
    const p = Number($('#sd-pct').value || 0);
    const after = basePremium * (1 - p / 100);
    const gst = after * 0.18;
    $('#sd-after').textContent = inr(after);
    $('#sd-gst').textContent = inr(gst);
    $('#sd-total').textContent = inr(after + gst);
  };
  $('#sd-pct').addEventListener('input', preview);
  preview();
  // select-for-UW radio (persisted client-side; the chosen version id rides into UW actions)
  root().querySelectorAll('input[name="v3-sel-uw"]').forEach(r => r.addEventListener('change', () => {
    setSavedVersion(caseId, Number(r.value));
    root().querySelectorAll('[data-vrow]').forEach(el =>
      el.classList.toggle('selected', Number(el.dataset.vrow) === Number(r.value)));
  }));
  // submit discount request
  $('#sd-send').addEventListener('click', async () => {
    const p = Number($('#sd-pct').value || 0);
    const remarks = $('#sd-remarks').value.trim();
    const st = $('#sd-status');
    if (!p) { st.textContent = 'Enter a requested discount %.'; return; }
    if (!remarks) { st.textContent = 'Justification remarks are mandatory.'; return; }
    st.textContent = 'Sending…';
    try {
      await post(`/api/v1/cases/${caseId}/discounts`, { RequestedPct: p, Remarks: remarks, JustificationType: 'CommercialDiscount' });
      st.textContent = '✓ Discount request sent to UW.';
      setTimeout(() => openSalesDrawer(caseId), 900);
    } catch (ex) { st.textContent = 'Failed: ' + ex.message; }
  });
}

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
  root().innerHTML = `
  <div class="v3-stepper" id="v3-stepper">${STEP_NAMES.map((n, i) =>
    `<div class="v3-step ${step === i + 1 ? 'active' : step > i + 1 ? 'done' : ''}" data-step="${i + 1}"><span>${String(i + 1).padStart(2, '0')}</span><b>${n}</b></div>`).join('')}</div>
  <section class="v3-panel active" id="v3-panel"></section>
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
  root().querySelectorAll('.v3-step').forEach(el => {
    el.classList.toggle('active', Number(el.dataset.step) === n);
    el.classList.toggle('done', Number(el.dataset.step) < n);
  });
  navBtns(n);
  const panel = $('#v3-panel');
  if (!CASE) { await newCaseStep(n, panel); return; }
  const id = CASE.caseId;
  try {
    if (n === 1) return renderUpload(panel);
    if (n === 2) return await renderMapping(panel, id);
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
    await uploadFile(`/api/v1/cases/${caseId}/documents/upload`, inp.files[0], docType);
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
    try {
      await uploadFile(`/api/v1/cases/${CASE.caseId}/documents/upload`, e.target.files[0], e.target.dataset.replace);
      showStep(1);
    } catch { alert('Upload failed'); }
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

/* ── step 3: standardize ── */
async function renderStandardize(panel, id) {
  panel.innerHTML = '<div class="ox-skeleton" style="height:160px"></div>';
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
          <td class="v3-small">${esc(String(r.diagnosisText || '').slice(0, 40))}</td></tr>`).join('') || '<tr><td colspan="5" class="v3-small">Run the pipeline first.</td></tr>'}</tbody>
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


/* ── step 5: P5 analytics — 5 sub-tabs ── */
async function renderAnalytics(panel, id) {
  panel.innerHTML = `
  <h1 class="ox-display" style="font-size:24px">5. Underwriting Analytics & Credibility Engine</h1>
  <div id="v3-ana-host"><div class="ox-skeleton" style="height:200px"></div></div>`;
  const host = $('#v3-ana-host');
  try {
    const [d, my, si] = await Promise.all([
      api(`/api/v1/cases/${id}/analytics`),
      api(`/api/v1/cases/${id}/multiyear`).catch(() => null),
      api(`/api/v1/cases/${id}/si-scenario`).catch(() => null),
    ]);
    if (!d.computed) { host.innerHTML = `<div class="ox-banner ox-banner--warn">${esc(d.message || 'Run the pipeline first.')}</div>`; return; }
    const a = d.snapshot;
    const bar = (label, val, max, fmt) => `
      <div style="margin-bottom:8px">
        <div class="ox-row" style="justify-content:space-between;font-size:12px"><span class="v3-small">${esc(label)}</span><span class="ox-num">${fmt(val)}</span></div>
        <div class="v3-bar"><span style="width:${Math.min(100, max > 0 ? (Number(val) / max) * 100 : 0)}%"></span></div>
      </div>`;
    const dictBars = (dict, fmt = (v) => Number(v).toFixed(1) + '%') => {
      const ent = Object.entries(dict || {}).sort((x, y) => y[1] - x[1]);
      const max = Math.max(0, ...ent.map(e => Math.abs(Number(e[1]))));
      return ent.map(([k, v]) => bar(k, v, max, fmt)).join('') || '<p class="v3-small">—</p>';
    };
    const siBandRows = (si?.bands || []).map(b => `
      <tr><td><b>${esc(b.label)}</b></td><td class="ox-num">${inr(b.historicalCost)}</td><td class="ox-num">${b.claimCount ?? 0}</td>
      <td class="ox-num">${b.realizationFactor != null ? Math.round(b.realizationFactor * 100) + '%' : '—'}</td>
      <td class="ox-num">${inr(b.incrementalCost)}</td></tr>`).join('') || '<tr><td colspan="5" class="v3-small">Run the pipeline first.</td></tr>';
    const siChronic = (si?.chronicClaims ?? 0);
    const siInc = si?.incrementalPerLife;
    const siApplied = !!si?.applyToPricing;
    const siComputed = !!si?.computed;

    host.innerHTML = `
    <div class="v3-grid" style="margin-bottom:16px">
      <div class="v3-card v3-c3"><div class="v3-small">Covered Lives</div><div class="v3-metric">${a.coveredLives ?? '—'}</div></div>
      <div class="v3-card v3-c3"><div class="v3-small">Claims (Paid)</div><div class="v3-metric">${a.claimCount ?? '—'} <span class="v3-small">(${a.paidClaimCount ?? '—'})</span></div></div>
      <div class="v3-card v3-c3"><div class="v3-small">Burn / Life</div><div class="v3-metric">${inr(a.burnCostPerLife)}</div></div>
      <div class="v3-card v3-c3"><div class="v3-small">Frequency / 100</div><div class="v3-metric">${a.frequencyPer100 != null ? Number(a.frequencyPer100).toFixed(1) : '—'}</div></div>
    </div>
    <div class="v3-subtabs" id="v3-p5-tabs">
      ${[['all', 'All Analytics (Complete)'], ['exp', '01 · 3-Year & SI Experience'], ['risk', '02 · Disease & Demography Risk'], ['scenario', '03 · Chronic SI Scenario Lab'], ['cred', '04 · Cell Credibility Engine']]
        .map(([k, l]) => `<button class="v3-subtab ${P5_TAB === k ? 'active' : ''}" data-t="${k}">${l}</button>`).join('')}
    </div>
    <div class="v3-grid" id="v3-p5-grid">
      <div class="v3-card v3-c12" data-p5tab="exp">
        <div class="ox-row" style="justify-content:space-between">
          <div><h3 class="ox-display">3-Year Company Experience</h3>
          <div class="v3-small">Enrollment exposure, paid claims, frequency and burn cost by policy year.</div></div>
        </div>
        <div class="ox-table-wrap" style="margin-top:10px"><table class="ox-table">
          <thead><tr><th>Policy Year</th><th>Lives</th><th>Paid Claims</th><th>Paid ₹</th><th>Frequency</th><th>Burn / Life</th></tr></thead>
          <tbody>${(my?.years || []).map(y => `
            <tr><td><b>${esc(y.policyYear)}</b></td><td class="ox-num">${y.lives}</td><td class="ox-num">${y.paidClaims}</td>
            <td class="ox-num">${inr(y.paidAmount)}</td><td class="ox-num">${y.frequencyPct}%</td><td class="ox-num">${inr(y.burnCostPerLife)}</td></tr>`).join('')
            || '<tr><td colspan="6" class="v3-small">No multi-year data.</td></tr>'}</tbody>
        </table></div>
      </div>
      <div class="v3-card v3-c6" data-p5tab="exp"><h3 class="ox-display">Current Population by Age Band</h3><div style="margin-top:10px">${dictBars(a.byAgeBand)}</div></div>
      <div class="v3-card v3-c6" data-p5tab="exp"><h3 class="ox-display">Demography Mix (Relationship)</h3><div style="margin-top:10px">${dictBars(a.byRelationship)}</div></div>

      <div class="v3-card v3-c6" data-p5tab="risk"><h3 class="ox-display">Age-Band Claim Drivers</h3><div style="margin-top:10px">${dictBars(a.byAgeBand)}</div></div>
      <div class="v3-card v3-c6" data-p5tab="risk"><h3 class="ox-display">Top Disease Cost Drivers</h3><div style="margin-top:10px">${dictBars(a.byDiseaseGroup, (v) => Number(v).toFixed(1) + '% of cost')}</div></div>
      <div class="v3-card v3-c12" data-p5tab="risk"><h3 class="ox-display">Chronic & Critical Indicators</h3>
        <div class="v3-grid" style="margin-top:10px;gap:10px">
          ${[['Chronic prevalence', pct(a.chronicPrevalencePct)], ['Maternity share of cost', pct(a.maternitySharePct)],
             ['Parent share of lives', pct(a.parentSharePct)], ['55+ share of lives', pct(a.seniorAgeSharePct)],
             ['Non-network cost share', pct(a.nonNetworkSharePct)], ['Single largest claim', pct(a.singleLargestSharePct)],
             ['Top-5 claims share', pct(a.top5ClaimSharePct)], ['Large-claim (₹2L+) share', pct(a.largeClaimSharePct)],
             ['Avg age', a.avgAge != null ? Number(a.avgAge).toFixed(1) : '—'], ['Volatility CV', a.volatilityCv != null ? Number(a.volatilityCv).toFixed(2) : '—']]
            .map(([k, v]) => `<div class="v3-c3"><div class="v3-small">${k}</div><b class="ox-num">${v}</b></div>`).join('')}
        </div>
      </div>

      <div class="v3-card v3-c12" data-p5tab="scenario">
        <div class="ox-row" style="justify-content:space-between;align-items:flex-start">
          <div><h3 class="ox-display">Impact of Increasing Sum Insured — Chronic Cases</h3>
          <div class="v3-small">Historical observed experience is locked. Edit scenario realization factors to test how higher SI may release additional chronic claim severity.</div></div>
          <span class="ox-badge ${siApplied ? 'ox-badge--ok' : 'ox-badge--neutral'}">${siApplied ? 'APPLIED TO PRICING' : 'ANALYSIS ONLY'}</span>
        </div>
        <div class="v3-grid" style="margin-top:12px">
          <div class="v3-card v3-c4"><h3 class="ox-display">Saved scenario</h3>
            <p class="v3-small">${siComputed
              ? `Chronic claims: <b>${siChronic}</b> · Incremental / life: <b>${inr(siInc)}</b><br/>Factors saved server-side: 3→4 ${pct((si?.factors?.f34 ?? 1) * 100)}, 3→5 ${pct((si?.factors?.f35 ?? 1) * 100)}, 4→5 ${pct((si?.factors?.f45 ?? 1) * 100)}.`
              : 'Run the pipeline first — the scenario lab computes from standardized claims.'}</p>
            <div class="v3-pill-note" style="margin-top:8px">Edit factors below and Save — persisted on the case and applied to the pricing bridge when "apply" is checked.</div>
          </div>
          <div class="v3-card v3-c8">
            <div class="ox-table-wrap"><table class="ox-table">
              <thead><tr><th>SI Band Shift</th><th>Historical Cost</th><th>Claims</th><th>Realization Factor</th><th>Incremental Cost</th></tr></thead>
              <tbody>${siBandRows}</tbody>
            </table></div>
          </div>
        </div>
        ${siComputed ? `
        <div class="v3-grid" style="margin-top:14px">
          <div class="v3-c4"><label class="ox-mono-label">3L→4L REALIZATION (× HISTORICAL)</label><input class="ox-input" id="si-f34" type="number" min="0" max="5" step="0.05" value="${si?.factors?.f34 ?? 1}"/></div>
          <div class="v3-c4"><label class="ox-mono-label">3L→5L REALIZATION</label><input class="ox-input" id="si-f35" type="number" min="0" max="5" step="0.05" value="${si?.factors?.f35 ?? 1}"/></div>
          <div class="v3-c4"><label class="ox-mono-label">4L→5L REALIZATION</label><input class="ox-input" id="si-f45" type="number" min="0" max="5" step="0.05" value="${si?.factors?.f45 ?? 1}"/></div>
          <div class="v3-c6" style="margin-top:10px">
            <label class="ox-mono-label">APPLY SCENARIO TO PRICING?</label>
            <select class="ox-input" id="si-apply"><option value="false" ${!siApplied ? 'selected' : ''}>No — analysis only</option><option value="true" ${siApplied ? 'selected' : ''}>Yes — include in benefit impact</option></select>
          </div>
          <div class="v3-c6" style="margin-top:10px;display:flex;align-items:flex-end;justify-content:flex-end;gap:8px">
            <button class="ox-btn ox-btn--primary" id="si-save">Save Scenario</button>
          </div>
        </div>
        <div class="v3-small" id="si-status" style="margin-top:8px"></div>` : ''}
      </div>

      <div class="v3-card v3-c12" data-p5tab="cred">
        <div class="ox-row" style="justify-content:space-between">
          <div><h3 class="ox-display">Credibility-adjusted Burn (Cell Engine)</h3>
          <div class="v3-small">Customer experience is blended with the matching portfolio cell, not one portfolio-wide number. Medical trend applies after the credibility blend.</div></div>
          <span class="ox-badge ox-badge--warn">PRICING ENGINE</span>
        </div>
        <div class="v3-grid" style="margin-top:12px;gap:10px">
          ${[['Incurred ₹', inr(a.amountIncurred)], ['Claimant incidence', pct(a.claimantIncidencePct)],
             ['Avg claim size', inr(a.avgClaimSize)], ['Median claim size', inr(a.medianClaimSize)],
             ['ICR', pct(a.incurredClaimsRatioPct)], ['Cost / life', inr(a.costPerLife)]]
            .map(([k, v]) => `<div class="v3-c4"><div class="v3-small">${k}</div><b class="ox-num" style="font-size:16px">${v}</b></div>`).join('')}
        </div>
        <div class="v3-pill-note" style="margin-top:12px"><b>Cell formula:</b> retention-adjusted customer burn = historical customer disease burn × retained known patients / historical known patients; then adjusted burn = Z × customer burn + (1−Z) × portfolio burn. Per-cell results persist in the pricing run components (step 7).</div>
      </div>
      <div class="v3-card v3-c12" data-p5tab="cred"><h3 class="ox-display">Trend summary</h3>
        <p class="v3-small" style="margin-top:6px;white-space:pre-wrap">${esc(a.trendNarrative || '—')}</p>
      </div>
    </div>`;

    // sub-tab switching
    const applyP5 = () => host.querySelectorAll('[data-p5tab]').forEach(el => {
      el.style.display = (P5_TAB === 'all' || el.dataset.p5tab === P5_TAB) ? '' : 'none';
    });
    host.querySelectorAll('#v3-p5-tabs .v3-subtab').forEach(b => b.addEventListener('click', () => {
      P5_TAB = b.dataset.t;
      host.querySelectorAll('#v3-p5-tabs .v3-subtab').forEach(x => x.classList.toggle('active', x === b));
      applyP5();
    }));
    applyP5();
    // SI scenario save
    if (siComputed) $('#si-save')?.addEventListener('click', async () => {
      const st = $('#si-status');
      st.textContent = 'Saving…';
      try {
        const res = await post(`/api/v1/cases/${id}/si-scenario`, {
          Factor3To4: Number($('#si-f34').value || 1),
          Factor3To5: Number($('#si-f35').value || 1),
          Factor4To5: Number($('#si-f45').value || 1),
          ApplyToPricing: $('#si-apply').value === 'true',
        });
        st.textContent = '✓ ' + (res.note || 'Scenario saved.');
      } catch (ex) { st.textContent = 'Failed: ' + ex.message; }
    });
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
      <div class="v3-small">Quote Confidence</div><div class="v3-metric">${conf != null ? Math.round(conf * 100 > 100 ? conf : conf * 100) + '%' : '—'}</div>
      <div class="v3-bar" style="margin-top:6px"><span style="width:${conf != null ? Math.min(100, Math.round(conf * 100 > 100 ? conf : conf * 100)) : 0}%"></span></div>
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
  <div class="ox-row" style="margin-top:14px"><button class="ox-btn ox-btn--ghost" id="gr-admin">Configure thresholds in Admin Console →</button></div>`;
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
    ['Observed Burn / Life', inr(p.observedBurnPerLife)],
    ['Normalized Burn / Life', inr(p.normalizedBurnPerLife)],
    ['Ultimate Burn / Life', inr(p.ultimateBurnPerLife)],
    ['Trended Burn / Life', inr(p.trendedBurnPerLife)],
    ['Benefit Delta / Life', inr(p.benefitDeltaPerLife)],
    ['Net Technical / Life', inr(p.netTechnicalPerLife)],
    ['Loadings / Life', inr(p.loadingsPerLife)],
    ['Gross Premium / Life', inr(p.grossPremiumPerLife)],
  ];
  panel.innerHTML = `
  <h1 class="ox-display" style="font-size:24px">7. Rule Pricing & UW Intervention</h1>
  <div class="v3-grid">
    <div class="v3-card v3-c4"><div class="v3-small">Observed Burn / Life</div><div class="v3-metric">${inr(p.observedBurnPerLife)}</div></div>
    <div class="v3-card v3-c4"><div class="v3-small">Technical Premium</div><div class="v3-metric">${inr(qv?.pricing?.technicalPremium ?? qv?.grossPremium)}</div></div>
    <div class="v3-card v3-c4"><div class="v3-small">Gross Premium</div><div class="v3-metric">${inr(qv?.grossPremium)}</div></div>
    <div class="v3-card v3-c12"><h3 class="ox-display">Pricing Bridge</h3>
      <div class="ox-table-wrap"><table class="ox-table">
        <thead><tr><th>Component</th><th>Value</th></tr></thead>
        <tbody>${bridge.map(([l, v]) => `<tr><td>${l}</td><td class="ox-num">${v}</td></tr>`).join('')}</tbody>
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


/* ── step 8: P8 benefits — 5 sub-tabs ── */
async function renderBenefits(panel, id) {
  panel.innerHTML = '<div class="ox-skeleton" style="height:160px"></div>';
  let d, quotes = [];
  try {
    [d, quotes] = await Promise.all([
      api(`/api/v1/cases/${id}/benefits`),
      api(`/api/v1/cases/${id}/quotes`).catch(() => []),
    ]);
  } catch (ex) { panel.innerHTML = errBanner(ex); return; }
  const lines = d.lines || [];
  const sum = d.summary || {};
  const competitors = d.competitors || [];
  // classify disease sub-limits vs core benefits by name heuristics
  const SUBLIMIT_WORDS = /cataract|hernia|kidney|joint|maternity|normal delivery|c-?section|ambulance|room rent|room|co-?pay|disease|limit/i;
  const isSublimit = (l) => SUBLIMIT_WORDS.test(String(l.benefitName || l.name || ''));
  const coreLines = lines.filter(l => !isSublimit(l));
  const subLines = lines.filter(l => isSublimit(l));
  const current = quotes.find(q => q.status === 'Current') || quotes[quotes.length - 1];
  const savedQv = savedVersion(id);

  const benefitRow = (l) => `
    <tr>
      <td><b>${esc(l.benefitName || l.name)}</b>${l.rationale ? `<div class="v3-small" style="margin-top:3px">${esc(String(l.rationale).slice(0, 100))}</div>` : ''}</td>
      <td class="ox-num">${esc(l.expiringText ?? l.expiring ?? '—')}</td>
      <td class="ox-num">${esc(l.proposedText ?? l.proposed ?? '—')}</td>
      <td><span class="ox-badge ${l.changeType === 'Improved' ? 'ox-badge--ok' : l.changeType === 'Reduced' ? 'ox-badge--danger' : 'ox-badge--neutral'}">${esc(l.changeType || '—')}</span></td>
      <td class="ox-num">${inr(l.impactPerLife ?? l.pricingImpact)}</td>
    </tr>`;
  const emptyRow = (n) => `<tr><td colspan="${n}" class="v3-small">${d.computed ? '—' : (esc(d.message || 'Run the pipeline first.'))}</td></tr>`;

  panel.innerHTML = `
  <h1 class="ox-display" style="font-size:24px">8. Expiring vs Proposed Benefits</h1>
  <div class="v3-grid" style="margin-bottom:16px">
    <div class="v3-card v3-c12"><div class="v3-live-premium">
      <div class="ox-row" style="justify-content:space-between">
        <div><div class="v3-small">LIVE TECHNICAL PREMIUM</div><div class="v3-live-amount">${inr(current?.pricing?.technicalPremium ?? current?.grossPremium)}</div></div>
        <div style="text-align:right"><div class="v3-small">RATE / LIFE</div><div style="font-size:20px;font-weight:700">${current?.coveredLives ? inr((current.grossPremium || 0) / current.coveredLives) : '—'}</div></div>
      </div>
    </div></div>
  </div>
  ${d.computed ? `
  <div class="v3-grid" style="margin-bottom:16px">
    <div class="v3-card v3-c3"><div class="v3-small">Total Lines</div><div class="v3-metric">${sum.totalLines ?? lines.length}</div></div>
    <div class="v3-card v3-c3"><div class="v3-small">Improved</div><div class="v3-metric">${sum.improved ?? '—'}</div></div>
    <div class="v3-card v3-c3"><div class="v3-small">Net Impact / Life</div><div class="v3-metric">${inr(sum.netImpactPerLife)}</div></div>
    <div class="v3-card v3-c3"><div class="v3-small">New Lines</div><div class="v3-metric">${sum.newLines ?? 0}</div></div>
  </div>` : ''}
  <div class="v3-subtabs" id="v3-p8-tabs">
    ${[['all', 'All Benefit Schedules'], ['core', '01 · Core Benefits & SI Bands'], ['sublimits', '02 · Disease Sub-Limits'], ['bridge', '03 · Incremental Impact Bridge'], ['versions', '04 · Quote Versions']]
      .map(([k, l]) => `<button class="v3-subtab ${P8_TAB === k ? 'active' : ''}" data-t="${k}">${l}</button>`).join('')}
  </div>
  <div class="v3-grid" id="v3-p8-grid">
    <div class="v3-card v3-c12" data-p8tab="core">
      <h3 class="ox-display">Core Benefit Changes</h3>
      <div class="ox-table-wrap" style="margin-top:10px"><table class="ox-table">
        <thead><tr><th>Benefit</th><th>Expiring Policy</th><th>Proposed RFQ</th><th>Change</th><th>Pricing Impact</th></tr></thead>
        <tbody>${(coreLines.length ? coreLines : lines).map(benefitRow).join('') || emptyRow(5)}</tbody>
      </table></div>
    </div>
    <div class="v3-card v3-c12" data-p8tab="sublimits">
      <h3 class="ox-display">Disease & Procedure Sub-Limits</h3>
      <p class="v3-small">Cataract, hernia, maternity, room-rent and other capped benefit lines extracted from the expiring policy vs the proposed RFQ.</p>
      <div class="ox-table-wrap" style="margin-top:10px"><table class="ox-table">
        <thead><tr><th>Sub-Limit</th><th>Expiring</th><th>Proposed</th><th>Change</th><th>Pricing Impact</th></tr></thead>
        <tbody>${subLines.map(benefitRow).join('') || (d.computed ? '<tr><td colspan="5" class="v3-small">No sub-limit lines detected in the benefit delta.</td></tr>' : emptyRow(5))}</tbody>
      </table></div>
    </div>
    <div class="v3-card v3-c12" data-p8tab="bridge">
      <h3 class="ox-display">Incremental Benefit Impact Bridge</h3>
      <p class="v3-small">Each benefit delta line's incremental cost per life, summed into the adjusted burn. Co-pay logic: a proposed co-pay reduces the insurer's expected payable claim cost on the eligible claim base; reducing a co-pay increases burn accordingly.</p>
      <div class="ox-table-wrap" style="margin-top:10px"><table class="ox-table">
        <thead><tr><th>#</th><th>Benefit Line</th><th>Method</th><th>Impact / Life</th><th>Rationale</th></tr></thead>
        <tbody>${lines.map((l, i) => `
          <tr><td class="ox-num">${i + 1}</td><td><b>${esc(l.benefitName || l.name)}</b></td>
          <td>${esc(l.method || '—')}</td><td class="ox-num">${inr(l.impactPerLife ?? l.pricingImpact)}</td>
          <td class="v3-small">${esc(String(l.rationale || '—').slice(0, 120))}</td></tr>`).join('') || emptyRow(5)}</tbody>
        ${lines.length ? `<tfoot><tr><td></td><td colspan="3"><b>Net incremental impact / life</b></td><td class="ox-num"><b>${inr(sum.netImpactPerLife)}</b></td></tr></tfoot>` : ''}
      </table></div>
    </div>
    <div class="v3-card v3-c12" data-p8tab="versions" style="border:1px solid var(--ox-vivid)">
      <div class="ox-row" style="justify-content:space-between;align-items:flex-start">
        <div><h3 class="ox-display">Quote Versions</h3>
        <div class="v3-small">Create multiple cover combinations under the same Case ID. One version is required when sending a referral or discount request to Underwriting.</div></div>
      </div>
      <div style="margin-top:12px">${quotes.map(q => `
        <div class="v3-version-item ${savedQv === q.quoteVersionId ? 'selected' : ''}">
          <div class="v3-version-top">
            <div><b>Version ${q.versionNo}</b> <span class="ox-badge ${q.status === 'Current' ? 'ox-badge--ok' : 'ox-badge--neutral'}">${esc(q.status)}</span>
              <div class="v3-small" style="margin-top:4px">${esc((q.createdAtUtc || '').slice(0, 16).replace('T', ' '))} · by ${esc(q.createdBy || 'Pipeline')}</div></div>
            <div class="ox-row" style="gap:12px"><b class="ox-num">${inr(q.grossPremium)}</b>
            <label class="v3-small" style="display:flex;align-items:center;gap:6px;cursor:pointer"><input type="radio" name="v3-p8-sel" value="${q.quoteVersionId}" ${savedQv === q.quoteVersionId ? 'checked' : ''}/> Select for UW</label></div>
          </div>
        </div>`).join('') || '<p class="v3-small">No quote versions yet — run the pipeline first.</p>'}</div>
    </div>
  </div>
  <div class="v3-footerbar"><span class="v3-small">No version save required to preview the final quote.</span></div>`;

  const applyP8 = () => panel.querySelectorAll('[data-p8tab]').forEach(el => {
    el.style.display = (P8_TAB === 'all' || el.dataset.p8tab === P8_TAB) ? '' : 'none';
  });
  panel.querySelectorAll('#v3-p8-tabs .v3-subtab').forEach(b => b.addEventListener('click', () => {
    P8_TAB = b.dataset.t;
    panel.querySelectorAll('#v3-p8-tabs .v3-subtab').forEach(x => x.classList.toggle('active', x === b));
    applyP8();
  }));
  applyP8();
  panel.querySelectorAll('input[name="v3-p8-sel"]').forEach(r => r.addEventListener('change', () => {
    setSavedVersion(id, Number(r.value));
    panel.querySelectorAll('[data-p8tab="versions"] .v3-version-item').forEach(el => {
      const radio = el.querySelector('input[name="v3-p8-sel"]');
      el.classList.toggle('selected', radio && radio.checked);
    });
  }));
}

/* ── step 9: final quote ── */
async function renderFinalQuote(panel, id) {
  panel.innerHTML = '<div class="ox-skeleton" style="height:160px"></div>';
  let d, discounts = [];
  try {
    d = await api(`/api/v1/cases/${id}/final-quote`);
    discounts = await api(`/api/v1/cases/${id}/discounts`).catch(() => []);
  } catch (ex) { panel.innerHTML = errBanner(ex); return; }
  const q = d.current || {};
  const versions = d.quotes || [];
  const current = versions.find(v => v.status === 'Current');
  const gross = q.grossPremium ?? current?.grossPremium;
  const activeDiscount = discounts.find(x => x.status === 'Approved');
  const baseForGst = activeDiscount ? gross * (1 - (activeDiscount.approvedPct || 0) / 100) : gross;
  panel.innerHTML = `
  <h1 class="ox-display" style="font-size:24px">9. Final Quotation & Proposal</h1>
  <div class="v3-grid">
    <div class="v3-card v3-c12"><h3 class="ox-display">Quotation — ${esc(d.clientName || CASE.clientName)}</h3>
      <div class="ox-table-wrap"><table class="ox-table">
        <thead><tr><th>Component</th><th>Value</th></tr></thead>
        <tbody>
          <tr><td>Quote Version</td><td>V${q.versionNo ?? current?.versionNo ?? '—'} ${activeDiscount ? `<span class="ox-badge ox-badge--ok">Discount ${pct(activeDiscount.approvedPct)} approved</span>` : ''}</td></tr>
          <tr><td>Net Premium</td><td class="ox-num">${inr(q.netPremium || gross)}</td></tr>
          <tr><td>Gross Premium (pre-GST)</td><td class="ox-num">${inr(baseForGst)}</td></tr>
          <tr><td>GST 18%</td><td class="ox-num">${inr(baseForGst != null ? baseForGst * 0.18 : null)}</td></tr>
          <tr><td><b>Final Annual Premium (incl. GST)</b></td><td class="ox-num"><b>${inr(baseForGst != null ? baseForGst * 1.18 : null)}</b></td></tr>
        </tbody>
      </table></div>
      ${activeDiscount ? `<div class="v3-pill-note" style="margin-top:10px"><b>Commercial discount applied:</b> ${pct(activeDiscount.approvedPct)} approved by ${esc(activeDiscount.approvedByEmail || 'UW')} — ${esc(activeDiscount.approvalRemarks || '')}. Covers unchanged.</div>` : ''}
    </div>
    <div class="v3-card v3-c12"><h3 class="ox-display">Quote Version History</h3>
      <div style="margin-top:10px">${versions.map(v => `
        <div class="v3-version-item"><div class="v3-version-top">
          <div><b>Version ${v.versionNo}</b> <span class="ox-badge ${v.status === 'Current' ? 'ox-badge--ok' : 'ox-badge--neutral'}">${esc(v.status)}</span>
          <div class="v3-small" style="margin-top:4px">${esc((v.createdAtUtc || '').slice(0, 16).replace('T', ' '))} · by ${esc(v.createdBy || 'Pipeline')}</div></div>
          <b class="ox-num">${inr(v.grossPremium)}</b>
        </div></div>`).join('') || '<p class="v3-small">No versions.</p>'}</div>
    </div>
    <div class="v3-card v3-c12"><h3 class="ox-display">Artifacts</h3>
      <div class="ox-table-wrap"><table class="ox-table">
        <thead><tr><th>File</th><th>Type</th><th>Generated</th></tr></thead>
        <tbody>${(d.artifacts || []).map(a => `
          <tr><td class="ox-mono">${esc(a.fileName)}</td><td>${esc(a.artifactType)}</td><td class="v3-small">${esc((a.generatedAtUtc || '').slice(0, 16).replace('T', ' '))}</td></tr>`).join('')
          || '<tr><td colspan="3" class="v3-small">No artifacts yet.</td></tr>'}</tbody>
      </table></div>
    </div>
  </div>
  <div class="ox-row" style="margin-top:14px;justify-content:space-between">
    <button class="ox-btn ox-btn--ghost" id="fq-drawer">Sales case drawer →</button>
    <div class="ox-row">
      <button class="ox-btn ox-btn--ghost" onclick="window.print()">Print / PDF</button>
      <button class="ox-btn ox-btn--primary" id="fq-dl">Download JSON</button>
    </div>
  </div>`;
  $('#fq-dl').addEventListener('click', () => {
    const blob = new Blob([JSON.stringify(d, null, 2)], { type: 'application/json' });
    const a = document.createElement('a'); a.href = URL.createObjectURL(blob);
    a.download = `${CASE.caseNumber}-quote.json`; a.click();
  });
  $('#fq-drawer').addEventListener('click', () => openSalesDrawer(id));
}


/* ── UW referral workbench — 3 decisions: approve / manual quote / return ── */
async function openWorkbench(caseId) {
  root().innerHTML = '<div class="ox-skeleton" style="height:220px"></div>';
  let c, discounts = [];
  try {
    [c, discounts] = await Promise.all([
      api(`/api/v1/cases/${caseId}`),
      api(`/api/v1/cases/${caseId}/discounts`).catch(() => []),
    ]);
  } catch (ex) { root().innerHTML = errBanner(ex); return; }
  CASE = c;
  $('#v3-case-pill').textContent = c.caseNumber;
  const quotes = c.quoteVersions || [];
  const current = quotes.find(q => q.status === 'Current') || quotes[quotes.length - 1];
  const gr = current?.guardrails || c.guardrails;
  const failed = gr?.evaluations?.filter(e => !e.passed) || [];
  const pendingDiscounts = discounts.filter(d => d.status === 'Requested');

  root().innerHTML = `
  <div class="ox-row" style="justify-content:space-between;align-items:flex-start;margin-bottom:18px">
    <div>
      <div class="v3-small">Underwriting Referral</div>
      <h1 class="ox-display" style="font-size:24px;margin:2px 0">${esc(c.caseNumber)} — ${esc(c.clientName)}</h1>
      <p class="v3-small" style="margin:0">${failed.length ? esc(failed.map(e => e.message).join(' · ')) : 'Review & decide'}</p>
    </div>
    <button class="ox-btn ox-btn--ghost" id="wb-back">← Back to Queue</button>
  </div>
  <div class="v3-grid" style="margin-bottom:16px">
    <div class="v3-card v3-c3"><div class="v3-small">Status</div><b>${esc(c.status)}</b></div>
    <div class="v3-card v3-c3"><div class="v3-small">Stage</div><b>${esc(c.currentStage || '—')}</b></div>
    <div class="v3-card v3-c3"><div class="v3-small">Lives</div><b>${current?.coveredLives ?? '—'}</b></div>
    <div class="v3-card v3-c3"><div class="v3-small">Current Premium</div><b>${inr(current?.grossPremium)}</b></div>
  </div>
  ${pendingDiscounts.length ? `<div class="ox-banner ox-banner--warn" style="margin-bottom:16px" id="wb-discount-summary">
    <b>Pending commercial discount request:</b> ${pendingDiscounts.map(d =>
      `${pct(d.requestedPct)} by ${esc(d.requestedByEmail || 'sales')} — "${esc(String(d.requestedRemarks || '').slice(0, 80))}" (request #${d.discountRequestId})`).join(' · ')}
    <div class="ox-row" style="margin-top:10px;gap:8px">
      <button class="ox-btn ox-btn--primary ox-btn--sm" id="wb-disc-approve">Approve discount</button>
      <button class="ox-btn ox-btn--ghost ox-btn--sm" id="wb-disc-decline">Decline</button>
    </div>
    <div class="v3-small" id="wb-disc-status" style="margin-top:6px"></div>
  </div>` : ''}
  <div class="v3-card"><h2 class="ox-display">Underwriter Response</h2>
    <p class="v3-small">Choose the appropriate outcome for this referred case. The response goes back to the same Sales case.</p>
    <div class="v3-decision-grid">
      <button class="v3-decision-btn" data-uwdecision="approve"><b>✓ Approve</b>The system records <b>Approved</b> and the quote flows back to Sales.</button>
      <button class="v3-decision-btn" data-uwdecision="pricing"><b>⚙ Modify Pricing Rules</b>Open the Rule Pricing page. Applied overrides make the final decision "Approved with Changes".</button>
      <button class="v3-decision-btn" data-uwdecision="manual"><b>📎 Manual Quote</b>Out-of-scope case: attach an Excel/PDF manual quote with UW remarks.</button>
      <button class="v3-decision-btn" data-uwdecision="return"><b>✕ Declined / Returned</b>Decline the case or return it to Sales for clarification with a due date.</button>
    </div>

    <div id="wb-approve-pane" class="ox-hide" style="margin-top:16px">
      <label class="ox-mono-label">UW APPROVAL REMARKS *</label>
      <textarea class="ox-input" id="wb-remarks" rows="3" placeholder="Example: Reviewed large-claim concentration. Current quote is acceptable within UW authority."></textarea>
      <div class="v3-grid" style="margin-top:10px">
        <div class="v3-c6"><label class="ox-mono-label">APPROVAL CONDITIONS (OPTIONAL)</label><input class="ox-input" id="wb-conditions" placeholder="Example: Subject to final census"/></div>
        <div class="v3-c6"><label class="ox-mono-label">APPROVAL AUTHORITY</label>
          <select class="ox-input" id="wb-authority"><option>Underwriter</option><option>Senior Underwriter</option><option>Chief Underwriter</option></select></div>
      </div>
      <div class="v3-footerbar"><button class="ox-btn ox-btn--primary" id="wb-confirm">Approve & Send to Sales</button></div>
    </div>

    <div id="wb-manual-pane" class="ox-hide" style="margin-top:16px">
      <div class="v3-pill-note"><b>Manual / out-of-scope response:</b> the standard engine remains as the audit trail, while the attached manual quote becomes the UW response to Sales.</div>
      <div class="v3-grid" style="margin-top:10px">
        <div class="v3-c6"><label class="ox-mono-label">MANUAL QUOTED PREMIUM ₹ (OPTIONAL)</label><input class="ox-input" id="wb-mq-premium" type="number" step="1000"/></div>
        <div class="v3-c6"><label class="ox-mono-label">DECISION</label>
          <select class="ox-input" id="wb-mq-decision"><option>Approved with Changes - Manual Quote</option><option>Approved with Conditions</option></select></div>
        <div class="v3-c12" style="margin-top:10px"><label class="ox-mono-label">UW REMARKS / RATIONALE *</label>
          <textarea class="ox-input" id="wb-mq-remarks" rows="3" placeholder="Explain why the standard engine is not applicable and summarize the manual terms."></textarea></div>
        <div class="v3-c12" style="margin-top:10px"><label class="ox-mono-label">ATTACH MANUAL QUOTE (EXCEL / PDF) *</label>
          <input class="ox-input" id="wb-mq-file" type="file" accept=".xlsx,.xls,.csv,.pdf"/></div>
      </div>
      <div class="v3-footerbar"><button class="ox-btn ox-btn--primary" id="wb-mq-send">Send Manual Quote to Sales</button></div>
      <div class="v3-small" id="wb-mq-status" style="margin-top:8px"></div>
    </div>

    <div id="wb-return-pane" class="ox-hide" style="margin-top:16px">
      <div class="v3-grid">
        <div class="v3-c6"><label class="ox-mono-label">OUTCOME</label>
          <select class="ox-input" id="wb-ret-decision"><option>Returned for Clarification</option><option>Need More Information</option><option>Declined</option></select></div>
        <div class="v3-c6"><label class="ox-mono-label">REQUIRED BY DATE (OPTIONAL)</label><input class="ox-input" id="wb-ret-date" type="date"/></div>
        <div class="v3-c12" style="margin-top:10px"><label class="ox-mono-label">UW REMARKS / INFORMATION REQUIRED *</label>
          <textarea class="ox-input" id="wb-ret-remarks" rows="3" placeholder="Clearly state what Sales needs to obtain, or why the case is declined."></textarea></div>
      </div>
      <div class="v3-footerbar"><button class="ox-btn ox-btn--primary" id="wb-ret-send">Send Response to Sales</button></div>
      <div class="v3-small" id="wb-ret-status" style="margin-top:8px"></div>
    </div>
  </div>`;

  $('#wb-back').addEventListener('click', renderUWQueue);

  // decision selector
  const selectDecision = (type) => {
    root().querySelectorAll('.v3-decision-btn').forEach(b =>
      b.classList.toggle('active', b.dataset.uwdecision === type));
    $('#wb-approve-pane').classList.toggle('ox-hide', type !== 'approve');
    $('#wb-manual-pane').classList.toggle('ox-hide', type !== 'manual');
    $('#wb-return-pane').classList.toggle('ox-hide', type !== 'return');
  };
  root().querySelectorAll('.v3-decision-btn').forEach(b => b.addEventListener('click', () => {
    const t = b.dataset.uwdecision;
    if (t === 'pricing') { openStepper(caseId) || setTimeout(() => showStep(7), 50); return; }
    selectDecision(t);
  }));

  // 1) approve — remarks + conditions + authority
  $('#wb-confirm').addEventListener('click', async () => {
    const remarks = $('#wb-remarks').value.trim();
    if (!remarks) return alert('Approval remarks are required');
    const conditions = $('#wb-conditions').value.trim();
    const authority = $('#wb-authority').value;
    const full = conditions ? `${remarks} [Conditions: ${conditions} · Authority: ${authority}]` : `${remarks} [Authority: ${authority}]`;
    try {
      await post('/api/v1/uw/decisions', { caseId, action: 'Approve', remarks: full });
      await renderUWQueue();
    } catch (ex) { alert('Decision failed: ' + ex.message); }
  });

  // 2) manual quote — decision select + premium + remarks + Excel/PDF attach
  $('#wb-mq-send').addEventListener('click', async () => {
    const st = $('#wb-mq-status');
    const remarks = $('#wb-mq-remarks').value.trim();
    const decision = $('#wb-mq-decision').value;
    const premium = $('#wb-mq-premium').value;
    const file = $('#wb-mq-file').files[0];
    if (!remarks) { st.textContent = 'UW remarks are mandatory.'; return; }
    if (!file) { st.textContent = 'Attach the manual Excel/PDF quote.'; return; }
    st.textContent = 'Uploading manual quote…';
    try {
      // record the decision first (audit trail), then attach the artifact to the case
      await post('/api/v1/uw/decisions', { caseId, action: 'ManualQuote', remarks: `${decision} · ${remarks}${premium ? ` · Manual premium ₹${Number(premium).toLocaleString('en-IN')}` : ''}` });
      await uploadFile(`/api/v1/cases/${caseId}/documents/upload`, file, 'UwManualQuote');
      st.textContent = '✓ Manual quote sent to Sales with attachment.';
      setTimeout(() => renderUWQueue(), 900);
    } catch (ex) { st.textContent = 'Failed: ' + ex.message; }
  });

  // 3) return — decision select + remarks + due date
  $('#wb-ret-send').addEventListener('click', async () => {
    const st = $('#wb-ret-status');
    const outcome = $('#wb-ret-decision').value;
    const remarks = $('#wb-ret-remarks').value.trim();
    const due = $('#wb-ret-date').value;
    if (!remarks) { st.textContent = 'UW remarks / information required are mandatory.'; return; }
    const full = `${outcome}. ${remarks}${due ? ` Required by ${due}.` : ''}`;
    st.textContent = 'Sending…';
    try {
      await post('/api/v1/uw/decisions', { caseId, action: 'ReturnForClarification', remarks: full });
      st.textContent = '✓ Response sent to Sales.';
      setTimeout(() => renderUWQueue(), 900);
    } catch (ex) { st.textContent = 'Failed: ' + ex.message; }
  });

  // pending discount decision (authority matrix on the backend: UW 5% / Senior 15% / Chief 50%)
  if (pendingDiscounts.length) {
    $('#wb-disc-approve')?.addEventListener('click', async () => {
      const st = $('#wb-disc-status');
      const d = pendingDiscounts[0];
      st.textContent = 'Approving…';
      try {
        await post(`/api/v1/discounts/${d.discountRequestId}/decide`, { ApprovedPct: d.requestedPct, Remarks: 'Approved from UW workbench.' });
        st.textContent = '✓ Discount approved.';
        setTimeout(() => openWorkbench(caseId), 900);
      } catch (ex) { st.textContent = 'Failed: ' + ex.message; }
    });
    $('#wb-disc-decline')?.addEventListener('click', async () => {
      const st = $('#wb-disc-status');
      const d = pendingDiscounts[0];
      st.textContent = 'Declining…';
      try {
        await post(`/api/v1/discounts/${d.discountRequestId}/decide`, { ApprovedPct: null, Remarks: 'Declined from UW workbench.' });
        st.textContent = '✓ Discount declined.';
        setTimeout(() => openWorkbench(caseId), 900);
      } catch (ex) { st.textContent = 'Failed: ' + ex.message; }
    });
  }
}

/* ── admin console — 4 tabs: formula / guardrails / excel masters / rule classification ── */
// parameter → rule mapping for the Guardrail Configurator (proto param ↔ our ruleCode)
const GRD_PARAMS = [
  { id: 'gMinLives',        label: 'Minimum covered lives',              rule: 'GRD-MIN-LIVES' },
  { id: 'gMinClaims',       label: 'Minimum claim count',                rule: 'GRD-MIN-CLAIMS' },
  { id: 'gMinDQ',           label: 'Minimum data quality %',             rule: 'GRD-DQ-SCORE' },
  { id: 'gMinMap',          label: 'Minimum mapping confidence %',       rule: 'GRD-MAPPING-CONF' },
  { id: 'gMinCred',         label: 'Minimum credibility Z %',            rule: null },
  { id: 'gHistoryMonths',   label: 'Minimum claims history months',      rule: null },
  { id: 'gMaxVolatility',   label: 'Maximum claim CV / volatility',      rule: 'GRD-VOLATILITY' },
  { id: 'gTop5Share',       label: 'Maximum top-5 claim share %',        rule: 'GRD-TOP5' },
  { id: 'gLargeShare',      label: 'Maximum 2L+ claim cost share %',     rule: 'GRD-LARGE-CLAIM' },
  { id: 'gSingleClaimShare',label: 'Maximum single claim share %',       rule: 'GRD-SINGLE-LARGEST' },
  { id: 'gMaxAvgAge',       label: 'Maximum average age',                rule: 'GRD-AVG-AGE' },
  { id: 'gAge55Share',      label: 'Maximum age 55+ share %',            rule: 'GRD-SENIOR-SHARE' },
  { id: 'gDependentShare',  label: 'Maximum dependent share %',          rule: null },
  { id: 'gParentShare',     label: 'Maximum parent share %',             rule: 'GRD-PARENT-SHARE' },
  { id: 'gPedShare',        label: 'Maximum PED-linked claim share %',   rule: 'GRD-PED-SHARE' },
  { id: 'gChronicShare',    label: 'Maximum chronic claim share %',      rule: 'GRD-CHRONIC-PREVALENCE' },
  { id: 'gMaternityShare',  label: 'Maximum maternity claim share %',    rule: 'GRD-MATERNITY-SHARE' },
  { id: 'gDiseaseShare',    label: 'Maximum one disease-group share %',  rule: 'GRD-DISEASE-CONC' },
  { id: 'gMaxSI',           label: 'Maximum Sum Insured ₹',              rule: null },
  { id: 'gMaxSIIncrease',   label: 'Maximum SI increase % vs expiring',  rule: 'GRD-SI-INCREASE' },
  { id: 'gBenefitDeltaPct', label: 'Maximum benefit-cost delta %',       rule: 'GRD-BENEFIT-DELTA' },
  { id: 'gNonStdCount',     label: 'Max non-standard benefit changes',   rule: 'GRD-NON-STANDARD-BEN' },
  { id: 'gMaxCommission',   label: 'Maximum commission %',               rule: null },
  { id: 'gMaxDiscount',     label: 'Maximum discount / negative load %', rule: null },
  { id: 'gPremiumDrop',     label: 'Maximum premium reduction %',        rule: null },
  { id: 'gMinRatePerLife',  label: 'Minimum rate per life ₹',            rule: 'GRD-RATE-PER-LIFE' },
];
const GRD_EXTRA = [
  { rule: 'GRD-NON-NETWORK', label: 'Maximum non-network claims cost share %' },
];

async function renderAdminConsole() {
  root().innerHTML = '<div class="ox-skeleton" style="height:180px"></div>';
  $('#v3-case-pill').textContent = 'Admin Console';
  let f = { TrendPct: 8, IbnrPct: 5 };
  let rules = [];
  try {
    [f, rules] = await Promise.all([
      api('/api/v1/admin/formulas').catch(() => f),
      api('/api/v1/admin/guardrail-rules').catch(() => []),
    ]);
  } catch { /* defaults */ }
  const ruleByCode = Object.fromEntries(rules.map(r => [r.ruleCode, r]));
  const masterVer = ruleByCode['GRD-MIN-LIVES'] ? 'Live (data-driven rules)' : 'Seed defaults';

  root().innerHTML = `
  <div class="ox-row" style="justify-content:space-between;margin-bottom:20px">
    <div><h1 class="ox-display" style="font-size:26px;margin:0">UW Admin Console</h1>
    <p class="v3-small" style="margin:4px 0 0">Govern pricing rules and underwriting masters. Formula rules are configured here; table-based masters are maintained by controlled Excel upload.</p></div>
    <button class="ox-btn ox-btn--ghost" id="adm-back">← Back to UW Queue</button>
  </div>
  <div class="v3-grid" style="margin-bottom:16px">
    <div class="v3-card v3-c3"><div class="v3-small">Data-driven guardrail rules</div><div class="v3-metric">${rules.length}</div></div>
    <div class="v3-card v3-c3"><div class="v3-small">Excel-based masters</div><div class="v3-metric">3</div></div>
    <div class="v3-card v3-c3"><div class="v3-small">Rule source</div><div style="margin-top:10px"><span class="ox-badge ox-badge--ok">${esc(masterVer)}</span></div></div>
    <div class="v3-card v3-c3"><div class="v3-small">Engine</div><div style="margin-top:10px"><span class="ox-badge ox-badge--neutral">deterministic core</span></div></div>
  </div>
  <div class="v3-admin-tabs">
    <button class="v3-admin-tab ${ADMIN_TAB === 'formula' ? 'active' : ''}" data-t="formula">Pricing Formula Configurator</button>
    <button class="v3-admin-tab ${ADMIN_TAB === 'guardrails' ? 'active' : ''}" data-t="guardrails">Guardrail Configurator</button>
    <button class="v3-admin-tab ${ADMIN_TAB === 'excel' ? 'active' : ''}" data-t="excel">Excel Masters</button>
    <button class="v3-admin-tab ${ADMIN_TAB === 'map' ? 'active' : ''}" data-t="map">Rule Classification</button>
  </div>
  <div id="adm-formula-panel" style="margin-top:14px">
    <div class="v3-pill-note" style="margin-bottom:14px"><b>Formula-based:</b> core actuarial assumptions applied during burn calculation on the next pipeline run.</div>
    <div class="v3-grid">
      <div class="v3-card v3-c6"><h3 class="ox-display">Medical Trend <span class="v3-type-tag">FORMULA</span></h3>
        <p class="v3-small">Base medical trend % applied in the pricing bridge.</p>
        <label class="ox-mono-label" style="margin-top:10px">BASE TREND %</label>
        <input class="ox-input v3-formula-input" id="fm-trend" type="number" step="0.25" value="${f.TrendPct ?? 8}"/>
      </div>
      <div class="v3-card v3-c6"><h3 class="ox-display">IBNR / Development <span class="v3-type-tag">FORMULA</span></h3>
        <p class="v3-small">Default IBNR % applied where claim-volume credibility is limited.</p>
        <label class="ox-mono-label" style="margin-top:10px">DEFAULT IBNR %</label>
        <input class="ox-input v3-formula-input" id="fm-ibnr" type="number" step="0.25" value="${f.IbnrPct ?? 5}"/>
      </div>
    </div>
    <div class="v3-footerbar">
      <span class="v3-small" id="fm-status"></span>
      <button class="ox-btn ox-btn--primary" id="fm-save">Save & Activate Formula Rules</button>
    </div>
  </div>

  <div id="adm-guardrails-panel" class="ox-hide" style="margin-top:14px">
    <div class="v3-pill-note" style="margin-bottom:14px"><b>Guardrails:</b> these rules decide whether a case continues as Auto Quote or moves to UW Referral. Threshold guardrails are configured here; each maps to a data-driven rule row.</div>
    <div class="v3-admin-grid" id="adm-grd-grid">
      <div class="v3-admin-group"><h3>1. Data & Experience Sufficiency <span class="v3-type-tag">AUTO REFERRAL</span></h3></div>
      ${GRD_PARAMS.slice(0, 6).map(p => grdCard(p, ruleByCode)).join('')}
      <div class="v3-admin-group"><h3>2. Experience Volatility <span class="v3-type-tag">AUTO REFERRAL</span></h3></div>
      ${GRD_PARAMS.slice(6, 10).map(p => grdCard(p, ruleByCode)).join('')}
      <div class="v3-admin-group"><h3>3. Portfolio Risk Concentration <span class="v3-type-tag">AUTO REFERRAL</span></h3></div>
      ${GRD_PARAMS.slice(10, 18).map(p => grdCard(p, ruleByCode)).join('')}
      <div class="v3-admin-group"><h3>4. Benefit / Commercial Deviation <span class="v3-type-tag">AUTO REFERRAL</span></h3></div>
      ${GRD_PARAMS.slice(18, 26).map(p => grdCard(p, ruleByCode)).join('')}
      <div class="v3-admin-group"><h3>5. Additional Live Rules <span class="v3-type-tag">ENGINE</span></h3></div>
      ${GRD_EXTRA.map(x => grdExtraCard(x, ruleByCode)).join('')}
    </div>
    <div class="v3-footerbar">
      <span class="v3-small" id="grd-status"></span>
      <button class="ox-btn ox-btn--primary" id="grd-save">Save & Activate Guardrails</button>
    </div>
  </div>

  <div id="adm-excel-panel" class="ox-hide" style="margin-top:14px">
    <div class="v3-pill-note" style="margin-bottom:14px"><b>Excel-based:</b> use this for reference tables that expand by product, channel, benefit or loading. Upload a masters workbook (xlsx/csv); named sheets or header-sniffing route rows into Benefit Costing / Commission / Standard Loading masters.</div>
    <div class="v3-grid">
      <div class="v3-card v3-c7">
        <h3 class="ox-display">Masters Workbook Upload</h3>
        <label class="ox-mono-label" style="margin-top:10px">WORKBOOK (.XLSX / .CSV, ≤ 20 MB)</label>
        <input class="ox-input" type="file" id="adm-masters-file" accept=".xlsx,.xls,.csv"/>
        <div class="ox-row" style="margin-top:12px;justify-content:flex-end">
          <button class="ox-btn ox-btn--primary" id="adm-masters-upload">Upload & Upsert Masters</button>
        </div>
        <div class="v3-small" id="adm-masters-status" style="margin-top:8px"></div>
      </div>
      <div class="v3-card v3-c5">
        <h3 class="ox-display">Recognized sheets</h3>
        <div class="ox-table-wrap"><table class="ox-table">
          <thead><tr><th>Sheet</th><th>Master Table</th></tr></thead>
          <tbody>
            <tr><td><b>BenefitCosting</b></td><td>Benefit × old/new limit × utilization matrix</td></tr>
            <tr><td><b>Commission</b></td><td>Channel/partner/business-type matrix</td></tr>
            <tr><td><b>StandardLoading</b></td><td>Named loading table</td></tr>
          </tbody>
        </table></div>
        <p class="v3-small" style="margin-top:8px">Unknown sheets are skipped. Flat CSVs are routed by header sniffing (BenefitCode / Channel / LoadingName). Master rows apply on the next pricing run.</p>
      </div>
    </div>
  </div>

  <div id="adm-map-panel" class="ox-hide" style="margin-top:14px">
    <div class="v3-card"><h2 class="ox-display">Current Rule Classification</h2>
      <div class="ox-table-wrap"><table class="ox-table">
        <thead><tr><th>Current rule / master</th><th>Governance type</th><th>Why</th><th>Consumed by</th></tr></thead>
        <tbody>
          <tr><td>Medical Trend / IBNR</td><td><b>Formula</b></td><td>Transparent single-value assumptions</td><td>Technical burn projection · ultimate burn</td></tr>
          <tr><td>Guardrail rules (${rules.length} live)</td><td><b>Formula</b></td><td>Transparent pass/fail thresholds, data-driven per rule row</td><td>Auto quote / referral decision</td></tr>
          <tr><td>Benefit Costing Master</td><td><b>Excel</b></td><td>Benefit × old/new limit × utilization matrix</td><td>Sparse-experience benefit costing</td></tr>
          <tr><td>Commission Master</td><td><b>Excel</b></td><td>Channel/partner/business-type matrix</td><td>Sales commission authority</td></tr>
          <tr><td>Standard Loading Master</td><td><b>Excel</b></td><td>Named loading reference table</td><td>Gross premium loadings</td></tr>
          <tr><td>Disease Taxonomy</td><td><b>Code</b></td><td>Deterministic clinical mapping in the engine</td><td>Disease mapping (step 4)</td></tr>
          <tr><td>Authority Matrix</td><td><b>Code</b></td><td>Role/deviation approval limits (UW 5% · Senior 15% · Chief 50%)</td><td>Discount & decision governance</td></tr>
        </tbody>
      </table></div>
    </div>
  </div>`;

  // tab switching
  const panels = { formula: '#adm-formula-panel', guardrails: '#adm-guardrails-panel', excel: '#adm-excel-panel', map: '#adm-map-panel' };
  const showTab = (t) => {
    ADMIN_TAB = t;
    root().querySelectorAll('.v3-admin-tab').forEach(b => b.classList.toggle('active', b.dataset.t === t));
    Object.entries(panels).forEach(([k, sel]) => $(sel).classList.toggle('ox-hide', k !== t));
  };
  root().querySelectorAll('.v3-admin-tab').forEach(b => b.addEventListener('click', () => showTab(b.dataset.t)));
  showTab(ADMIN_TAB);
  $('#adm-back').addEventListener('click', renderUWQueue);

  // formula save
  $('#fm-save').addEventListener('click', async () => {
    const st = $('#fm-status');
    st.textContent = 'Saving…';
    try {
      await post('/api/v1/admin/formulas', { TrendPct: Number($('#fm-trend').value), IbnrPct: Number($('#fm-ibnr').value) });
      st.textContent = '✓ Saved — applies on the next pipeline run.';
    } catch (ex) { st.textContent = 'Failed: ' + ex.message; }
  });

  // guardrail save — one upsert per mapped rule
  $('#grd-save').addEventListener('click', async () => {
    const st = $('#grd-status');
    st.textContent = 'Saving…';
    let ok = 0, fail = 0, skipped = 0;
    try {
      for (const p of GRD_PARAMS) {
        if (!p.rule) { skipped++; continue; }                 // no live rule yet — prototype-only param
        const inp = $(`#${p.id}`);
        if (!inp) { skipped++; continue; }
        const rule = ruleByCode[p.rule];
        const val = Number(inp.value);
        if (Number.isNaN(val) || val === Number(rule.threshold)) { skipped++; continue; }
        await post('/api/v1/admin/guardrail-rules', {
          RuleCode: rule.ruleCode, RuleName: rule.ruleName, Category: rule.category,
          MetricSelector: rule.metricSelector, Operator: rule.operator, Threshold: val,
          Severity: rule.severity, MessageTemplate: rule.messageTemplate,
          AppliesToExperienceBusiness: rule.appliesToExperienceBusiness, IsEnabled: rule.isEnabled,
        });
        ok++;
      }
      st.textContent = `✓ Saved ${ok} threshold(s)${fail ? `, ${fail} failed` : ''}${skipped ? ` · ${skipped} unchanged/unmapped` : ''} — applies on the next guardrail evaluation.`;
    } catch (ex) { st.textContent = `Failed after ${ok} saves: ${ex.message}`; }
  });

  // excel masters upload
  $('#adm-masters-upload').addEventListener('click', async () => {
    const st = $('#adm-masters-status');
    const file = $('#adm-masters-file').files[0];
    if (!file) { st.textContent = 'Choose a workbook first.'; return; }
    st.textContent = 'Uploading…';
    try {
      const res = await uploadFile('/api/v1/admin/masters/upload', file);
      const sheets = Object.entries(res.sheets || {}).map(([k, v]) => `${k}: ${v}`).join(' · ');
      st.textContent = `✓ ${res.fileName} → master ${res.masterVersion}. ${sheets}`;
    } catch (ex) { st.textContent = 'Failed: ' + ex.message; }
  });
}

// one admin card per prototype threshold param; disabled when no live rule backs it
function grdCard(p, ruleByCode) {
  const rule = p.rule ? ruleByCode[p.rule] : null;
  const val = rule ? rule.threshold : '';
  const op = rule ? rule.operator : '';
  const sev = rule ? rule.severity : '';
  return `<div class="v3-rule-card">
    <h3>${esc(p.label)} ${rule ? `<span class="v3-type-tag">LIVE</span>` : `<span class="v3-type-tag excel">PROTOTYPE</span>`}</h3>
    ${rule ? `<p class="v3-small">${esc(rule.ruleCode)} · ${esc(op)} threshold · ${esc(sev)}</p>` : `<p class="v3-small">No engine rule yet — displayed for parity with the prototype.</p>`}
    <label class="ox-mono-label" style="margin-top:8px">THRESHOLD</label>
    <input class="ox-input v3-formula-input" id="${p.id}" type="number" step="any" value="${val}" ${rule ? '' : 'disabled'}/>
  </div>`;
}
function grdExtraCard(x, ruleByCode) {
  const rule = ruleByCode[x.rule];
  return `<div class="v3-rule-card">
    <h3>${esc(x.label)} <span class="v3-type-tag">LIVE</span></h3>
    <p class="v3-small">${esc(rule?.ruleCode || x.rule)} · ${esc(rule?.operator || '')} threshold · ${esc(rule?.severity || '')}</p>
    <label class="ox-mono-label" style="margin-top:8px">THRESHOLD</label>
    <input class="ox-input v3-formula-input" id="x-${x.rule}" type="number" step="any" value="${rule ? rule.threshold : ''}"/>
  </div>`;
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
