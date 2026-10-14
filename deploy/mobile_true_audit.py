"""True-device mobile audit via laptop Chrome CDP (real 390px viewport, real media queries).
Audits every GMC page: overflow, KPI wrap, table stacking, tap targets, drawer fit."""
import json, urllib.request, urllib.error, time, base64, sys

CDP = "http://127.0.0.1:9223"
SITE = "https://gmc.darvis.si"
OUT = "/home/ubuntu/gmc-v2-deploy/qa-shots"

def put(path, data):
    r = urllib.request.Request(CDP + path, data=json.dumps(data).encode(),
                               headers={"Content-Type": "application/json"}, method="PUT" if False else "POST")
    try:
        with urllib.request.urlopen(r, timeout=60) as resp:
            return json.loads(resp.read().decode() or "{}")
    except urllib.error.HTTPError as e:
        return {"err": e.code}

def get(path):
    try:
        with urllib.request.urlopen(CDP + path, timeout=30) as resp:
            return json.loads(resp.read().decode() or "{}")
    except urllib.error.HTTPError as e:
        return {"err": e.code}

# find or create the target tab
tabs = get("/json/list")
if isinstance(tabs, dict): tabs = tabs.get("tabs", [])
tgt = next((t for t in tabs if isinstance(t, dict) and t.get("type") == "page" and "gmc.darvis.si" in t.get("url", "")), None)
if tgt is None:
    tgt = get("/json/new?" + SITE + "/login")
    if isinstance(tgt, str):
        import json as _j; tgt = _j.loads(tgt)
tid = tgt["id"]
WS = None  # use HTTP endpoints only (json/new, json/activate) + Runtime.evaluate needs ws… fallback: use /json/new + screenshot endpoint? No: use ws.

# Minimal websocket client (stdlib only)
import socket, struct, os, hashlib, base64 as b64
class WS:
    def __init__(self, url):
        assert url.startswith("ws://")
        rest = url[5:]; host, path = rest.split("/", 1); path = "/" + path
        self.sock = socket.create_connection((host.split(":")[0], int(host.split(":")[1]) if ":" in host else 80), timeout=30)
        key = b64.b64encode(os.urandom(16)).decode()
        self.sock.sendall((f"GET {path} HTTP/1.1\r\nHost: {host}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n").encode())
        resp = b""
        while b"\r\n\r\n" not in resp: resp += self.sock.recv(4096)
        assert b"101" in resp.split(b"\r\n")[0], resp[:200]
        self.buf = b""
    def send(self, payload):
        data = payload.encode()
        header = bytearray([0x81])
        n = len(data)
        if n < 126: header.append(0x80 | n)
        elif n < 65536: header.append(0x80 | 126); header += struct.pack(">H", n)
        else: header.append(0x80 | 127); header += struct.pack(">Q", n)
        mask = os.urandom(4); header += mask
        data = bytes(b ^ mask[i % 4] for i, b in enumerate(data))
        self.sock.sendall(bytes(header) + data)
    def recv_raw(self, timeout=60):
        self.sock.settimeout(timeout)
        hdr = self._read(2)
        op = hdr[0] & 0x0F; ln = hdr[1] & 0x7F
        if ln == 126: ln = struct.unpack(">H", self._read(2))[0]
        elif ln == 127: ln = struct.unpack(">Q", self._read(8))[0]
        data = self._read(ln) if ln else b""
        return op, data
    def _read(self, n):
        while len(self.buf) < n:
            chunk = self.sock.recv(65536)
            if not chunk: raise ConnectionError("ws closed")
            self.buf += chunk
        out, self.buf = self.buf[:n], self.buf[n:]
        return out
    def recv_msg(self, timeout=60):
        op, data = self.recv_raw(timeout)
        if op == 9:  # ping -> pong
            self.sock.sendall(b"\x8a\x80" + os.urandom(4))
            return self.recv_msg(timeout)
        return data.decode(errors="replace")

ws_url = tgt.get("webSocketDebuggerUrl")
if not ws_url:
    ws_url = f"ws://127.0.0.1:9223/devtools/page/{tid}"
ws = WS(ws_url)
mid = [0]
def cdp(method, params=None, timeout=60):
    mid[0] += 1
    ws.send(json.dumps({"id": mid[0], "method": method, "params": params or {}}))
    deadline = time.time() + timeout
    while time.time() < deadline:
        line = ws.recv_msg(timeout)
        try: msg = json.loads(line)
        except Exception: continue
        if msg.get("id") == mid[0]:
            return msg.get("result", msg)
    return {"err": "timeout"}

def js(expr, timeout=60):
    r = cdp("Runtime.evaluate", {"expression": expr, "returnByValue": True, "awaitPromise": True}, timeout)
    return r.get("result", {}).get("value")

# mobile emulation: real 390px
cdp("Emulation.setDeviceMetricsOverride", {"width": 390, "height": 844, "deviceScaleFactor": 2, "mobile": True})
cdp("Emulation.setTouchEmulationEnabled", {"enabled": True})

def shot(name):
    r = cdp("Page.captureScreenshot", {"format": "png"}, timeout=60)
    if "data" in r:
        open(f"{OUT}/{name}.png", "wb").write(base64.b64decode(r["data"]))
        return True
    return False

def login():
    js(f"location.href='{SITE}/login'")
    time.sleep(3)
    js("""(async()=>{const c=[...document.querySelectorAll('button')].find(b=>b.textContent.trim()==='Sales');if(c)c.click();await new Promise(r=>setTimeout(r,400));const s=[...document.querySelectorAll('button')].find(b=>b.textContent.trim()==='Sign in');if(s)s.click();return 'ok'})()""")
    time.sleep(3)

login()
AUDIT = r"""
(function(){
  const vw = innerWidth, out = {vw, overflows: [], kpiCols: 0, gridCols: 0, tables: [], tiny: []};
  document.querySelectorAll('.ox-content *').forEach(el => {
    const r = el.getBoundingClientRect();
    if (r.width === 0) return;
    const wrap = el.closest('.ox-table-wrap');
    if (wrap && wrap !== el) return;
    if (r.right > vw + 1) out.overflows.push(((el.className||el.tagName)+'').toString().split(' ')[0]+' r'+Math.round(r.right));
  });
  out.overflows = [...new Set(out.overflows)].slice(0, 6);
  const kpi = document.querySelector('.ox-kpi-row');
  if (kpi) out.kpiCols = getComputedStyle(kpi).gridTemplateColumns.split(' ').length;
  const g2 = document.querySelector('.ox-grid-2');
  if (g2) out.gridCols = getComputedStyle(g2).gridTemplateColumns.split(' ').length;
  document.querySelectorAll('table.ox-table').forEach(t => {
    const tr = t.querySelector('tbody tr');
    if (tr) out.tables.push(getComputedStyle(tr).display === 'block' ? 'stacked' : 'hscroll('+t.querySelectorAll('tbody tr').length+'r)');
  });
  document.querySelectorAll('.ox-btn, .ox-nav-item, .ox-bell, .ox-burger').forEach(el => {
    const r = el.getBoundingClientRect();
    if (r.width > 0 && (r.height < 34 || r.width < 34)) out.tiny.push(((el.className||'?')+'').split(' ')[0]+' '+Math.round(r.width)+'x'+Math.round(r.height));
  });
  out.tiny = out.tiny.slice(0, 5);
  out.pageOverflow = document.documentElement.scrollWidth > vw + 1;
  return JSON.stringify(out);
})()
"""

report = []
ROUTES = [("#/", "cases"), ("#/case/30", "case-detail"), ("#/exceptions", "exceptions"),
          ("#/users", "users"), ("#/admin", "admin"), ("#/about", "about")]
for route, name in ROUTES:
    js(f"location.hash='{route}'")
    time.sleep(2.4)
    res = js(AUDIT) or "{}"
    shot(f"m390-{name}")
    print(f"── {name}: {res[:360]}")
    report.append((name, res))

# drawer
js("location.hash='#/'"); time.sleep(2)
js("document.getElementById('new-case')?.click()")
time.sleep(1.2)
res = js("(function(){const d=document.getElementById('drawer');const r=d.getBoundingClientRect();return JSON.stringify({x:Math.round(r.x),w:Math.round(r.width),vw:innerWidth,fits:r.x>=0&&r.right<=innerWidth+1,open:d.classList.contains('open')})})()")
shot("m390-drawer")
print("── drawer:", res[:200])

print("\nDone — screenshots in", OUT)
