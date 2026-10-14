"""Drive laptop Chrome via CDP (through SSH tunnel) with true 390px device emulation.
Full-page mobile QA: login, sweep every page, capture screenshots + layout metrics."""
import json, base64, time, sys, urllib.request

try:
    from websocket import create_connection
except ImportError:
    sys.exit("websocket-client missing")

CDP = "http://127.0.0.1:9223"
OUT = "/home/ubuntu/gmc-v2-deploy/qa-shots"

def http(path, timeout=8):
    """CDP HTTP via SSH-side curl (Chrome blocks non-localhost Host on /json)."""
    import subprocess
    try:
        out = subprocess.run(["ssh", "darvis-laptop", f"curl -s --max-time 5 -H 'Host: localhost' http://localhost:9222{path}"],
                             capture_output=True, text=True, timeout=timeout)
        return json.loads(out.stdout or "{}")
    except Exception as e:
        return {"err": str(e)[:120]}

# find or create a tab
targets = http("/json/list")
if isinstance(targets, dict) and "err" in targets:
    sys.exit("CDP list failed: " + targets["err"])
page = next((t for t in targets if t.get("type") == "page"), None)
if page is None:
    page = http("/json/new?about:blank")
# route the websocket through the tunnel (9223 local → 9222 on laptop)
WS = page["webSocketDebuggerUrl"].replace("ws://localhost:9222", "ws://127.0.0.1:9223") \
                                 .replace("ws://localhost/", "ws://127.0.0.1:9223/")
print("target:", page.get("url", "?")[:70])

ws = create_connection(WS, timeout=60)
mid = [0]
def cmd(method, params=None):
    mid[0] += 1
    ws.send(json.dumps({"id": mid[0], "method": method, "params": params or {}}))
    while True:
        m = json.loads(ws.recv())
        if m.get("id") == mid[0]:
            return m.get("result", {})

def js(expr, await_promise=False):
    r = cmd("Runtime.evaluate", {"expression": expr, "awaitPromise": await_promise, "returnByValue": True})
    v = r.get("result", {})
    if v.get("subtype") == "error": return "JSERR: " + str(v.get("description", ""))[:160]
    return v.get("value")

def nav(url):
    cmd("Page.navigate", {"url": url}); time.sleep(2.6)

def shot(name, full=False):
    p = {"format": "png"}
    if full:
        m = js("JSON.stringify({w:document.documentElement.scrollWidth,h:document.documentElement.scrollHeight})")
        try: d = json.loads(m)
        except Exception: d = {"w": 390, "h": 900}
        cmd("Emulation.setDeviceMetricsOverride", {"width": 390, "height": min(d["h"], 8000), "deviceScaleFactor": 2, "mobile": True})
        time.sleep(0.5)
    p["captureBeyondViewport"] = bool(full)
    r = cmd("Page.captureScreenshot", p)
    data = r.get("data")
    if data:
        open(f"{OUT}/{name}.png", "wb").write(base64.b64decode(data))
        print("shot:", name)
    # back to normal viewport
    if full:
        cmd("Emulation.setDeviceMetricsOverride", {"width": 390, "height": 844, "deviceScaleFactor": 2, "mobile": True})
        time.sleep(0.3)

# ── device emulation: iPhone 14-ish ──
cmd("Emulation.setDeviceMetricsOverride", {"width": 390, "height": 844, "deviceScaleFactor": 2, "mobile": True})
cmd("Emulation.setTouchEmulationEnabled", {"enabled": True, "maxTouchPoints": 5})
cmd("Emulation.setUserAgentOverride", {"userAgent": "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1"})
cmd("Page.enable"); cmd("Runtime.enable")

AUDIT = r"""
(function(){
  const vw = innerWidth, out = {vw, overflows: [], kpiCols: 0, gridCols: 0, tables: [], tiny: [], fonts: null};
  document.querySelectorAll('.ox-content *').forEach(el => {
    const r = el.getBoundingClientRect();
    if (r.width === 0) return;
    const wrap = el.closest('.ox-table-wrap');
    if (wrap && wrap !== el) return;
    if (r.right > vw + 1) out.overflows.push((String(el.className).slice(0,40)||el.tagName)+' r='+Math.round(r.right));
  });
  out.overflows = [...new Set(out.overflows)].slice(0, 6);
  const kpi = document.querySelector('.ox-kpi-row');
  if (kpi) out.kpiCols = getComputedStyle(kpi).gridTemplateColumns.split(' ').length;
  const g2 = document.querySelector('.ox-grid-2');
  if (g2) out.gridCols = getComputedStyle(g2).gridTemplateColumns.split(' ').length;
  document.querySelectorAll('table.ox-table').forEach(t => {
    const tr = t.querySelector('tbody tr');
    if (tr) out.tables.push(getComputedStyle(tr).display === 'block' ? 'stacked' : 'grid');
  });
  document.querySelectorAll('.ox-btn, .ox-nav-item, .ox-bell, .ox-burger').forEach(el => {
    const r = el.getBoundingClientRect();
    if (r.width > 0 && (r.height < 34 || r.width < 34)) out.tiny.push((String(el.className).split(' ')[0]||'?')+' '+Math.round(r.width)+'x'+Math.round(r.height));
  });
  out.tiny = out.tiny.slice(0, 5);
  out.pageOverflow = document.documentElement.scrollWidth > vw + 1;
  out.fonts = {cabinet: document.fonts.check('16px "Cabinet Grotesk"'), inter: document.fonts.check('16px Inter'), mono: document.fonts.check('12px "JetBrains Mono"')};
  out.burgerVisible = (()=>{const b=document.querySelector('.ox-burger'); return b? getComputedStyle(b).display : 'none';})();
  return JSON.stringify(out);
})()
"""

# login
nav("https://gmc.darvis.si/login")
js("(async()=>{const c=[...document.querySelectorAll('button')].find(b=>b.textContent.trim()==='Sales');if(c)c.click();await new Promise(r=>setTimeout(r,400));const s=[...document.querySelectorAll('button')].find(b=>b.textContent.trim()==='Sign in');if(s)s.click();return 'in'})()", True)
time.sleep(3)

ROUTES = [("#/", "m-cases"), ("#/case/30", "m-case-detail"), ("#/exceptions", "m-exceptions"),
          ("#/users", "m-users"), ("#/admin", "m-admin"), ("#/about", "m-about")]
results = {}
for route, name in ROUTES:
    nav("https://gmc.darvis.si/" + route)
    results[name] = js(AUDIT)
    shot(name, full=True)
    # viewport-only top shot too
    shot(name + "-top")

# drawer
nav("https://gmc.darvis.si/#/")
time.sleep(1.5)
js("document.getElementById('new-case')?.click();'1'")
time.sleep(1.2)
results["m-drawer"] = js("(function(){const d=document.getElementById('drawer');const r=d.getBoundingClientRect();return JSON.stringify({x:Math.round(r.x),w:Math.round(r.width),vw:innerWidth,open:d.classList.contains('open'),fits:r.x>=0&&r.right<=innerWidth+1})})()")
shot("m-drawer", full=True)

print(json.dumps({k: json.loads(v) if v.startswith('{') else v for k, v in results.items()}, indent=1)[:2600])
ws.close()
print("DONE")
