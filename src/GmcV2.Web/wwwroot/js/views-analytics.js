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
  let d;
  try { d = await api(`/api/v1/cases/${caseId}/analytics`); } catch (ex) {
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
