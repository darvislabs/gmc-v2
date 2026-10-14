// ── UW Analytics (prototype §5) ─────────────────────────────────────────────
async function viewAnalytics() {
  if (!canDecide()) { $('#view').innerHTML = '<div class="ox-banner ox-banner--error">Underwriters only.</div>'; return; }
  $('#pagetitle').textContent = 'UW Analytics';
  $('#view').innerHTML = '<div class="ox-skeleton" style="height:200px"></div>';
  const cases = await api('/api/v1/cases');
  const wantCase = new URLSearchParams(location.hash.split('?')[1] || '').get('case');
  const sel = wantCase ?? cases[0]?.caseId;
  if (!sel) { $('#view').innerHTML = '<div class="ox-empty-state"><p class="ox-mb-0">No cases yet.</p></div>'; return; }
  $('#view').innerHTML = `
    <div class="ox-row ox-row--between ox-row--wrap" style="margin-bottom:12px;gap:8px">
      <select class="ox-select" id="an-case" style="max-width:420px">${cases.map(c =>
        `<option value="${c.caseId}" ${String(c.caseId) === String(sel) ? 'selected' : ''}>${esc(c.caseNumber)} — ${esc(c.clientName)} (${esc(c.status)})</option>`).join('')}</select>
      <button class="ox-btn ox-btn--sm" id="an-open-si">SI scenarios →</button>
    </div>
    <div id="an-panel"></div>`;
  $('#an-case').addEventListener('change', () => { location.hash = `#/analytics?case=${$('#an-case').value}`; });
  $('#an-open-si').addEventListener('click', () => { location.hash = `#/case/${sel}`; });
  renderAnalyticsPanel(sel);
}

async function renderAnalyticsPanel(caseId) {
  const el = $('#an-panel');
  el.innerHTML = '<div class="ox-skeleton" style="height:160px"></div>';
  let d, my;
  try {
    [d, my] = await Promise.all([
      api(`/api/v1/cases/${caseId}/analytics`),
      api(`/api/v1/cases/${caseId}/multiyear`).catch(() => null),
    ]);
  } catch (ex) {
    el.innerHTML = `<div class="ox-banner ox-banner--error">${esc(ex.message)}</div>`; return;
  }
  if (!d.computed) { el.innerHTML = `<div class="ox-banner ox-banner--warn">${esc(d.message || 'Run the pipeline first.')}</div>`; return; }
  const a = d.snapshot;
  const bar = (label, val, max, fmt) => `
    <div style="margin-bottom:8px">
      <div class="ox-row ox-row--between" style="font-size:12px"><span class="ox-muted">${esc(label)}</span><span class="ox-num">${fmt(val)}</span></div>
      <div style="height:6px;background:var(--ox-gray);border-radius:3px;overflow:hidden">
        <div style="height:100%;width:${Math.min(100, max > 0 ? (val / max) * 100 : 0)}%;background:var(--ox-navy)"></div>
      </div>
    </div>`;
  const dictRows = (dict, fmt = (v) => v.toFixed(1) + '%') => Object.entries(dict || {})
    .sort((x, y) => y[1] - x[1]).map(([k, v]) => bar(k, v, Math.max(...Object.values(dict)), fmt)).join('') || '<p class="ox-muted">—</p>';

  el.innerHTML = `
  <div class="ox-kpis" style="margin-bottom:14px">
    ${[['COVERED LIVES', a.coveredLives], ['CLAIMS', a.claimCount], ['INCURRED ₹', inr(a.amountIncurred)],
       ['BURN / LIFE', inr(a.burnCostPerLife)], ['FREQ /100', (a.frequencyPer100 ?? 0).toFixed(1)],
       ['CHRONIC PREV', pct(a.chronicPrevalencePct)], ['LARGE-CLAIM COST', pct(a.largeClaimSharePct)], ['VOLATILITY CV', (a.volatilityCv ?? 0).toFixed(2)]]
      .map(([k, v]) => `<div class="ox-kpi"><div class="ox-mono-label">${k}</div><div class="ox-num">${esc(v)}</div></div>`).join('')}
  </div>
  <div class="ox-grid-2" style="gap:14px">
    <div class="ox-card">
      <div class="ox-mono-label">POPULATION BY AGE BAND</div>
      <div style="margin-top:10px">${dictRows(a.byAgeBand)}</div>
    </div>
    <div class="ox-card">
      <div class="ox-mono-label">DEMOGRAPHY MIX (RELATIONSHIP)</div>
      <div style="margin-top:10px">${dictRows(a.byRelationship)}</div>
    </div>
    <div class="ox-card">
      <div class="ox-mono-label">TOP DISEASE COST DRIVERS</div>
      <div style="margin-top:10px">${dictRows(a.byDiseaseGroup, (v) => v.toFixed(1) + '% of cost')}</div>
    </div>
    <div class="ox-card">
      <div class="ox-mono-label">CHRONIC & CRITICAL INDICATORS</div>
      <div class="ox-ana" style="margin-top:10px">
        ${[['Chronic prevalence', pct(a.chronicPrevalencePct)], ['Maternity share of cost', pct(a.maternitySharePct)],
           ['Parent share of lives', pct(a.parentSharePct)], ['55+ share of lives', pct(a.seniorAgeSharePct)],
           ['Non-network cost share', pct(a.nonNetworkSharePct)], ['Single largest claim', pct(a.singleLargestSharePct)],
           ['Top-5 claims', pct(a.top5ClaimSharePct)]]
          .map(([k, v]) => `<div class="ox-ana-row"><span class="ox-muted">${k}</span><span class="ox-num">${v}</span></div>`).join('')}
      </div>
    </div>
  </div>
  <div class="ox-card" style="margin-top:14px">
    <div class="ox-mono-label">TREND SUMMARY — ${d.siScenario ? 'SI SCENARIO SAVED' : 'EXPERIENCE READ'}</div>
    <p style="margin:8px 0 0;line-height:1.55">${esc(a.trendNarrative || '—')}</p>
    ${d.siScenario ? `<div class="ox-row" style="margin-top:10px;gap:14px;flex-wrap:wrap" class="ox-muted">
      <span class="ox-mono">3L→4L ×${d.siScenario.factor3To4}</span>
      <span class="ox-mono">3L→5L ×${d.siScenario.factor3To5}</span>
      <span class="ox-mono">4L→5L ×${d.siScenario.factor4To5}</span>
      <span class="ox-badge ${d.siScenario.applyToPricing ? 'ox-badge--ok' : 'ox-badge--warn'}">${d.siScenario.applyToPricing ? 'APPLIED TO PRICING' : 'ANALYSIS ONLY'}</span>
    </div>` : ''}
  </div>`;
}

// ── Benefit Delta (prototype §8) ────────────────────────────────────────────
async function viewBenefits() {
  if (!canDecide()) { $('#view').innerHTML = '<div class="ox-banner ox-banner--error">Underwriters only.</div>'; return; }
  $('#pagetitle').textContent = 'Benefit Delta';
  $('#view').innerHTML = '<div class="ox-skeleton" style="height:200px"></div>';
  const cases = await api('/api/v1/cases');
  const wantCase = new URLSearchParams(location.hash.split('?')[1] || '').get('case');
  const sel = wantCase ?? cases[0]?.caseId;
  if (!sel) { $('#view').innerHTML = '<div class="ox-empty-state"><p class="ox-mb-0">No cases yet.</p></div>'; return; }
  $('#view').innerHTML = `
    <div class="ox-row ox-row--between ox-row--wrap" style="margin-bottom:12px;gap:8px">
      <select class="ox-select" id="bd-case" style="max-width:420px">${cases.map(c =>
        `<option value="${c.caseId}" ${String(c.caseId) === String(sel) ? 'selected' : ''}>${esc(c.caseNumber)} — ${esc(c.clientName)} (${esc(c.status)})</option>`).join('')}</select>
    </div>
    <div id="bd-panel"></div>`;
  $('#bd-case').addEventListener('change', () => { location.hash = `#/benefits?case=${$('#bd-case').value}`; });
  renderBenefitsPanel(sel);
}

async function renderBenefitsPanel(caseId) {
  const el = $('#bd-panel');
  el.innerHTML = '<div class="ox-skeleton" style="height:160px"></div>';
  let d;
  try { d = await api(`/api/v1/cases/${caseId}/benefits`); } catch (ex) {
    el.innerHTML = `<div class="ox-banner ox-banner--error">${esc(ex.message)}</div>`; return;
  }
  if (!d.computed) { el.innerHTML = `<div class="ox-banner ox-banner--warn">${esc(d.message || 'Upload expiring + proposed benefit schedules first.')}</div>`; return; }
  const line = (r) => `
    <tr>
      <td>${esc(r.benefitName)}</td>
      <td class="ox-num">${esc(r.expiringText || '—')}</td>
      <td class="ox-num">${esc(r.proposedText || '—')}</td>
      <td>${r.changeType === 'Improved' ? '<span class="ox-badge ox-badge--ok">Improved</span>'
          : r.changeType === 'Reduced' ? '<span class="ox-badge ox-badge--error">Reduced</span>'
          : r.changeType === 'New' ? '<span class="ox-badge ox-badge--warn">New</span>'
          : '<span class="ox-badge">Unchanged</span>'}</td>
      <td class="ox-num">${r.impactPerLife != null ? inr(r.impactPerLife) : '—'}</td>
    </tr>`;
  el.innerHTML = `
  <div class="ox-kpis" style="margin-bottom:14px">
    ${[['LINES COMPARED', d.summary.totalLines], ['IMPROVED', d.summary.improved], ['REDUCED', d.summary.reduced],
       ['NEW', d.summary.newLines], ['NET IMPACT / LIFE', inr(d.summary.netImpactPerLife)]]
      .map(([k, v]) => `<div class="ox-kpi"><div class="ox-mono-label">${k}</div><div class="ox-num">${esc(v)}</div></div>`).join('')}
  </div>
  <div class="ox-card">
    <div class="ox-mono-label">EXPIRING VS PROPOSED — CORE BENEFIT CHANGES</div>
    <div class="ox-table-wrap" style="margin-top:10px">
      <table class="ox-table">
        <thead><tr><th>Benefit</th><th>Expiring</th><th>Proposed</th><th>Change</th><th>Impact / life</th></tr></thead>
        <tbody>${d.lines.map(line).join('') || '<tr><td colspan="5" class="ox-muted">No lines.</td></tr>'}</tbody>
      </table>
    </div>
  </div>
  ${d.competitors && d.competitors.length ? `
  <div class="ox-card" style="margin-top:14px">
    <div class="ox-mono-label">COMPETITION PREMIUM SUMMARY</div>
    <div class="ox-table-wrap" style="margin-top:10px">
      <table class="ox-table">
        <thead><tr><th>Competitor</th><th>SI</th><th>Premium</th><th>Δ vs ours</th></tr></thead>
        <tbody>${d.competitors.map((c) => `
          <tr><td>${esc(c.name)}</td><td class="ox-num">${inr(c.sumInsured)}</td>
          <td class="ox-num">${inr(c.premium)}</td>
          <td class="ox-num">${c.deltaPct != null ? pct(c.deltaPct) : '—'}</td></tr>`).join('')}</tbody>
      </table>
    </div>
  </div>` : ''}`;
}

// ── Final Quote (prototype §9): commission + discount + versions + decision ─
async function viewFinalQuote() {
  if (!canDecide()) { $('#view').innerHTML = '<div class="ox-banner ox-banner--error">Underwriters only.</div>'; return; }
  $('#pagetitle').textContent = 'Final Quote';
  $('#view').innerHTML = '<div class="ox-skeleton" style="height:200px"></div>';
  const cases = await api('/api/v1/cases');
  const wantCase = new URLSearchParams(location.hash.split('?')[1] || '').get('case');
  const sel = wantCase ?? cases[0]?.caseId;
  if (!sel) { $('#view').innerHTML = '<div class="ox-empty-state"><p class="ox-mb-0">No cases yet.</p></div>'; return; }
  $('#view').innerHTML = `
    <div class="ox-row ox-row--between ox-row--wrap" style="margin-bottom:12px;gap:8px">
      <select class="ox-select" id="fq-case" style="max-width:420px">${cases.map(c =>
        `<option value="${c.caseId}" ${String(c.caseId) === String(sel) ? 'selected' : ''}>${esc(c.caseNumber)} — ${esc(c.clientName)} (${esc(c.status)})</option>`).join('')}</select>
      <a class="ox-btn ox-btn--sm" href="#/case/${sel}">Full case →</a>
    </div>
    <div id="fq-panel"></div>`;
  $('#fq-case').addEventListener('change', () => { location.hash = `#/quote?case=${$('#fq-case').value}`; });
  renderFinalQuotePanel(sel);
}

async function renderFinalQuotePanel(caseId) {
  const el = $('#fq-panel');
  el.innerHTML = '<div class="ox-skeleton" style="height:160px"></div>';
  let d;
  try { d = await api(`/api/v1/cases/${caseId}/final-quote`); } catch (ex) {
    el.innerHTML = `<div class="ox-banner ox-banner--error">${esc(ex.message)}</div>`; return;
  }
  const money = (v) => v == null ? '—' : inr(v);
  el.innerHTML = `
  ${d.current ? `
  <div class="ox-kpis" style="margin-bottom:14px">
    ${[['CURRENT VERSION', 'V' + d.current.versionNo], ['TECHNICAL PREMIUM', money(d.current.technicalPremium)],
       ['GROSS PREMIUM', money(d.current.grossPremium)], ['NET PREMIUM', money(d.current.netPremium)],
       ['CASE STATUS', d.status]]
      .map(([k, v]) => `<div class="ox-kpi"><div class="ox-mono-label">${k}</div><div class="ox-num">${esc(v)}</div></div>`).join('')}
  </div>` : '<div class="ox-banner ox-banner--warn">No quote yet — run the pipeline.</div>'}
  <div class="ox-grid-2" style="gap:14px">
    <div class="ox-card">
      <div class="ox-mono-label">SALES COMMISSION SCHEDULE</div>
      <div class="ox-table-wrap" style="margin-top:10px">
        <table class="ox-table">
          <thead><tr><th>Channel</th><th>Default</th><th>Band</th></tr></thead>
          <tbody>${(d.commission || []).map((c) => `
            <tr><td>${esc(c.channelType || c.Item1)}</td>
            <td class="ox-num">${pct(c.defaultRatePct ?? c.Item2)}</td>
            <td class="ox-num">${pct(c.minRatePct ?? c.Item3)} – ${pct(c.maxRatePct ?? c.Item4)}</td></tr>`).join('') || '<tr><td colspan="3" class="ox-muted">—</td></tr>'}</tbody>
        </table>
      </div>
    </div>
    <div class="ox-card">
      <div class="ox-mono-label">COMMERCIAL DISCOUNT — REQUESTS & DECISIONS</div>
      <div style="margin-top:10px">${(d.discounts || []).map((r) => `
        <div class="ox-ana-row"><span class="ox-muted">${r.requestedPct}% · ${esc(r.justificationType || '')} · ${esc(r.status)}</span>
        <span class="ox-num">${r.approvedPct != null ? '→ ' + r.approvedPct + '%' : 'pending'}</span></div>`).join('') || '<p class="ox-muted">No discount requests.</p>'}
      </div>
    </div>
  </div>
  <div class="ox-card" style="margin-top:14px">
    <div class="ox-mono-label">QUOTE VERSIONS — FULL CHAIN</div>
    <div class="ox-table-wrap" style="margin-top:10px">
      <table class="ox-table">
        <thead><tr><th>V</th><th>Status</th><th>Technical</th><th>Gross</th><th>Net</th><th>By</th><th>When</th></tr></thead>
        <tbody>${(d.quotes || []).map((q) => `
          <tr><td class="ox-num">V${q.versionNo}</td>
          <td><span class="ox-badge ${q.status === 'Current' ? 'ox-badge--ok' : ''}">${esc(q.status)}</span></td>
          <td class="ox-num">${money(q.technicalPremium)}</td>
          <td class="ox-num">${money(q.grossPremium)}</td>
          <td class="ox-num">${money(q.netPremium)}</td>
          <td class="ox-muted">${esc(q.createdBy || '')}</td>
          <td class="ox-muted">${new Date(q.createdAtUtc).toLocaleString()}</td></tr>`).join('') || '<tr><td colspan="7" class="ox-muted">—</td></tr>'}</tbody>
      </table>
    </div>
  </div>
  ${(d.artifacts || []).length ? `
  <div class="ox-card" style="margin-top:14px">
    <div class="ox-mono-label">QUOTE ARTIFACTS</div>
    <div class="ox-table-wrap" style="margin-top:10px">
      <table class="ox-table">
        <thead><tr><th>Type</th><th>File</th><th>By</th><th>When</th></tr></thead>
        <tbody>${d.artifacts.map((a) => `
          <tr><td>${esc(a.artifactType)}</td><td>${esc(a.fileName)}</td>
          <td class="ox-muted">${esc(a.generatedBy || '')}</td>
          <td class="ox-muted">${new Date(a.generatedAtUtc).toLocaleString()}</td></tr>`).join('')}</tbody>
      </table>
    </div>
  </div>` : ''}`;
}

// ── Standardize & Validate (prototype §3) ───────────────────────────────────
async function viewStandardize() {
  if (!canDecide()) { $('#view').innerHTML = '<div class="ox-banner ox-banner--error">Underwriters only.</div>'; return; }
  $('#pagetitle').textContent = 'Standardize & Validate';
  $('#view').innerHTML = '<div class="ox-skeleton" style="height:200px"></div>';
  const cases = await api('/api/v1/cases');
  const wantCase = new URLSearchParams(location.hash.split('?')[1] || '').get('case');
  const sel = wantCase ?? cases[0]?.caseId;
  if (!sel) { $('#view').innerHTML = '<div class="ox-empty-state"><p class="ox-mb-0">No cases yet.</p></div>'; return; }
  $('#view').innerHTML = `
    <div class="ox-row ox-row--between ox-row--wrap" style="margin-bottom:12px;gap:8px">
      <select class="ox-select" id="std-case" style="max-width:420px">${cases.map(c =>
        `<option value="${c.caseId}" ${String(c.caseId) === String(sel) ? 'selected' : ''}>${esc(c.caseNumber)} — ${esc(c.clientName)} (${esc(c.status)})</option>`).join('')}</select>
    </div>
    <div id="std-panel"></div>`;
  $('#std-case').addEventListener('change', () => { location.hash = `#/standardize?case=${$('#std-case').value}`; });
  renderStandardizePanel(sel);
}

async function renderStandardizePanel(caseId) {
  const el = $('#std-panel');
  el.innerHTML = '<div class="ox-skeleton" style="height:160px"></div>';
  let d;
  try { d = await api(`/api/v1/cases/${caseId}/standardize`); } catch (ex) {
    el.innerHTML = `<div class="ox-banner ox-banner--error">${esc(ex.message)}</div>`; return;
  }
  if (!d.computed) { el.innerHTML = `<div class="ox-banner ox-banner--warn">${esc(d.message || 'Run the pipeline first.')}</div>`; return; }
  const check = (label, ok, detail) => `
    <div class="ox-ana-row"><span class="ox-muted">${esc(label)}</span>
    <span class="ox-num">${ok ? '<span class="ox-badge ox-badge--ok">PASS</span>' : '<span class="ox-badge ox-badge--warn">CHECK</span>'} <span class="ox-muted">${esc(detail)}</span></span></div>`;
  el.innerHTML = `
  <div class="ox-kpis" style="margin-bottom:14px">
    ${[['MEMBERS', d.members], ['CLAIMS', d.claims], ['DQ SCORE', d.dqScore != null ? (d.dqScore * 100).toFixed(0) + '%' : '—'],
       ['MAPPING CONF', d.mappingConfidence != null ? (d.mappingConfidence * 100).toFixed(0) + '%' : '—']]
      .map(([k, v]) => `<div class="ox-kpi"><div class="ox-mono-label">${k}</div><div class="ox-num">${esc(v)}</div></div>`).join('')}
  </div>
  <div class="ox-grid-2" style="gap:14px">
    <div class="ox-card">
      <div class="ox-mono-label">VALIDATION CHECKS</div>
      <div class="ox-ana" style="margin-top:10px">
        ${[['Member↔claim linkage', d.linkagePct != null, d.linkagePct != null ? (d.linkagePct).toFixed(0) + '%' : '—'],
           ['Claim status codes', true, d.paidClaims + ' paid / ' + (d.outstandingClaims || 0) + ' outstanding'],
           ['Age bands populated', d.ageBandsPopulated, (d.ageBandsPopulated ? 'all' : 'partial')],
           ['SI populated on members', d.siPopulated, d.siPopulated ? 'yes' : 'missing — defaults applied']]
          .map(([l, ok, det]) => check(l, ok, det)).join('')}
      </div>
    </div>
    <div class="ox-card">
      <div class="ox-mono-label">NORMALIZED PREVIEW — FIRST CLAIMS</div>
      <div class="ox-table-wrap" style="margin-top:10px">
        <table class="ox-table">
          <thead><tr><th>Claim</th><th>Member</th><th>Status</th><th>Paid ₹</th><th>Diagnosis</th></tr></thead>
          <tbody>${(d.preview || []).map((r) => `
            <tr><td>${esc(r.claimNumber)}</td><td>${esc(r.memberNumber || '—')}</td>
            <td>${esc(r.claimStatus)}</td><td class="ox-num">${inr(r.paidAmount)}</td>
            <td class="ox-muted">${esc((r.diagnosisText || '').slice(0, 40))}</td></tr>`).join('') || '<tr><td colspan="5" class="ox-muted">—</td></tr>'}</tbody>
        </table>
      </div>
    </div>
  </div>
  ${my && my.years && my.years.length ? `
  <div class="ox-card" style="margin-top:14px">
    <div class="ox-mono-label">3-YEAR COMPANY EXPERIENCE — EXPOSURE, FREQUENCY & BURN COST BY POLICY YEAR</div>
    <div class="ox-table-wrap" style="margin-top:10px">
      <table class="ox-table">
        <thead><tr><th>Policy Year</th><th>Lives</th><th>Paid Claims</th><th>Total Claims</th><th>Paid ₹</th><th>Frequency %</th><th>Burn Cost / Life</th></tr></thead>
        <tbody>${my.years.map((y) => `
          <tr><td><strong>${esc(y.policyYear)}</strong></td>
          <td class="ox-num">${y.lives}</td>
          <td class="ox-num">${y.paidClaims}</td>
          <td class="ox-num">${y.totalClaims}</td>
          <td class="ox-num">${inr(y.paidAmount)}</td>
          <td class="ox-num">${y.frequencyPct != null ? y.frequencyPct + '%' : '—'}</td>
          <td class="ox-num">${y.burnCostPerLife != null ? inr(y.burnCostPerLife) : '—'}</td></tr>`).join('')}
        </tbody>
      </table>
    </div>
    <div class="ox-muted" style="font-size:11px;margin-top:8px">Total 3-year paid: <span class="ox-num">${inr(my.totalPaid)}</span> · Latest experience year: ${esc(my.latestYear || '—')}</div>
  </div>` : ''}`;
}
