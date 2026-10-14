"""Measured mobile-layout audit (375px-equivalent via injected media rules).
Walks every page, reports: page h-overflow, elements crossing viewport, KPI wrapping,
tap targets, table stacking, drawer fit. Deterministic bounding-box math."""
import json, urllib.request, urllib.error, time, sys

BASE = "http://localhost:9377"
SITE = "https://gmc.darvis.si"
USER = "qa-bot"

def req(method, path, data=None, timeout=90):
    body = json.dumps(data).encode() if data is not None else None
    r = urllib.request.Request(BASE + path, data=body, headers={"Content-Type": "application/json"}, method=method)
    try:
        with urllib.request.urlopen(r, timeout=timeout) as resp:
            return json.loads(resp.read().decode() or "{}")
    except urllib.error.HTTPError as e:
        return {"HTTP_ERROR": e.code, "body": e.read().decode()[:200]}

def ev(T, expr, tries=2):
    for _ in range(tries):
        r = req("POST", f"/tabs/{T}/evaluate", {"expression": expr, "userId": USER})
        if "HTTP_ERROR" not in r: return r.get("result", "")
        time.sleep(1.2)
    return "EVAL-FAIL"

AUDIT_JS = r"""
(function(){
  const vw = innerWidth, out = {vw, overflows: [], stickyBroken: 0, kpiCols: 0, gridCols: 0, tables: []};
  // 1. elements crossing the viewport right edge (excluding intentional scroll containers' children)
  document.querySelectorAll('.ox-content *').forEach(el => {
    const r = el.getBoundingClientRect();
    if (r.width === 0) return;
    const wrap = el.closest('.ox-table-wrap');
    if (wrap && wrap !== el) return; // intentional h-scroll inside table wraps
    if (r.right > vw + 1) out.overflows.push((el.className||el.tagName)+' r='+Math.round(r.right));
  });
  out.overflows = [...new Set(out.overflows)].slice(0, 8);
  // 2. KPI row: how many columns actually rendered
  const kpi = document.querySelector('.ox-kpi-row');
  if (kpi) out.kpiCols = getComputedStyle(kpi).gridTemplateColumns.split(' ').length;
  // 3. first grid-2 columns
  const g2 = document.querySelector('.ox-grid-2');
  if (g2) out.gridCols = getComputedStyle(g2).gridTemplateColumns.split(' ').length;
  // 4. tables: stacked (tr display block) or horizontal
  document.querySelectorAll('table.ox-table').forEach(t => {
    const tr = t.querySelector('tbody tr');
    if (tr) out.tables.push({stacked: getComputedStyle(tr).display === 'block', rows: t.querySelectorAll('tbody tr').length});
  });
  // 5. tap targets < 40px on interactive elements
  out.tiny = [];
  document.querySelectorAll('.ox-btn, .ox-nav-item, .ox-bell, .ox-burger').forEach(el => {
    const r = el.getBoundingClientRect();
    if (r.width > 0 && (r.height < 36 || r.width < 36)) out.tiny.push((el.className.split(' ')[0]||'?')+' '+Math.round(r.width)+'x'+Math.round(r.height));
  });
  out.tiny = out.tiny.slice(0, 6);
  out.pageOverflow = document.documentElement.scrollWidth > vw + 1;
  return JSON.stringify(out);
})()
"""

T = sys.argv[1] if len(sys.argv) > 1 else open('/tmp/qatab').read().strip()
req("POST", f"/tabs/{T}/evaluate", {"expression": "(function(){const sheet=document.styleSheets[0];let block=null;for(const r of sheet.cssRules){if(r.media&&r.media.mediaText.includes('768'))block=r.cssText;}if(!block)return 'NO-BLOCK';const st=document.createElement('style');st.id='qa-m375';st.textContent=block.replaceAll('(max-width: 768px)','(max-width: 3000px)');document.head.appendChild(st);return 'm375-active'})()", "userId": USER})
time.sleep(0.6)

ROUTES = [("#/", "cases"), ("#/case/30", "case-detail"), ("#/exceptions", "exceptions"),
          ("#/users", "users"), ("#/admin", "admin"), ("#/about", "about")]
allbad = []
for route, name in ROUTES:
    req("POST", f"/tabs/{T}/navigate", {"url": SITE + "/" + route, "userId": USER})
    time.sleep(2.4)
    res = ev(T, AUDIT_JS)
    print(f"── {name}: {res[:400]}")
    try:
        d = json.loads(res)
        if d.get("pageOverflow") or d.get("overflows") or d.get("tiny"): allbad.append((name, d))
        if d.get("kpiCols") not in (0, 1, 2) and name in ("cases", "exceptions"): allbad.append((name, "kpiCols="+str(d.get("kpiCols"))))
    except Exception: allbad.append((name, res[:100]))

# drawer fit
req("POST", f"/tabs/{T}/navigate", {"url": SITE + "/#/", "userId": USER}); time.sleep(2)
ev(T, "document.getElementById('new-case')?.click();'1'")
time.sleep(1.2)
res = ev(T, "(function(){const d=document.getElementById('drawer');const r=d.getBoundingClientRect();return JSON.stringify({x:Math.round(r.x),w:Math.round(r.width),vw:innerWidth,fits:r.x>=0&&r.right<=innerWidth+1,open:d.classList.contains('open')})})()")
print("── drawer:", res[:200])

print("\n==== PAGES WITH ISSUES:", len(allbad))
for n, d in allbad: print(" *", n, json.dumps(d)[:220])
