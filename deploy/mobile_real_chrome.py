"""True mobile QA on Dhrumil's laptop Chrome via CDP (tunneled :9223).
Device emulation 390x844 DPR2 touch + real media queries + real webfonts.
Sweeps every console page, measures layout defects, captures screenshots."""
import json, base64, time, urllib.request

CDP = "http://127.0.0.1:9223"
OUT = "/home/ubuntu/gmc-v2-deploy/qa-shots-mobile"
SITE = "https://gmc.darvis.si"

def ws_connect(ws_url):
    """minimal websocket client over stdlib"""
    import socket, base64 as b64, struct, os
    from urllib.parse import urlparse
    u = urlparse(ws_url)
    sock = socket.create_connection((u.hostname, u.port), timeout=15)
    key = b64.b64encode(os.urandom(16)).decode()
    req = (f"GET {u.path} HTTP/1.1\r\nHost: {u.hostname}:{u.port}\r\n"
           f"Upgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\n"
           f"Sec-WebSocket-Version: 13\r\n\r\n")
    sock.sendall(req.encode())
    resp = b""
    while b"\r\n\r\n" not in resp:
        resp += sock.recv(4096)
    return sock

class CDPClient:
    def __init__(self, ws_url):
        self.sock = ws_connect(ws_url)
        self.buf = b""
        self.mid = 0
    def _recv_exact(self, n):
        while len(self.buf) < n:
            chunk = self.sock.recv(65536)
            if not chunk: raise ConnectionError("closed")
            self.buf += chunk
        out, self.buf = self.buf[:n], self.buf[n:]
        return out
    def send(self, method, params=None):
        import struct, os
        self.mid += 1
        msg = json.dumps({"id": self.mid, "method": method, "params": params or {}}).encode()
        mask = os.urandom(4)
        payload = bytearray(mask)
        for i, b in enumerate(msg): payload.append(b ^ mask[i % 4])
        ln = len(msg)
        if ln < 126: hdr = struct.pack("!BB", 0x81, 0x80 | ln)
        elif ln < 65536: hdr = struct.pack("!BBH", 0x81, 0x80 | 126, ln)
        else: hdr = struct.pack("!BBQ", 0x81, 0x80 | 127, ln)
        self.sock.sendall(bytes(hdr) + bytes(payload))
        return self.mid
    def recv_msg(self, want_id=None, timeout=30):
        import struct
        deadline = time.time() + timeout
        msgs = []
        while time.time() < deadline:
            h = self._recv_exact(2)
            opcode, masked_len = h[0] & 0x0F, h[1] & 0x7F
            ln = masked_len
            if ln == 126: ln = struct.unpack("!H", self._recv_exact(2))[0]
            elif ln == 127: ln = struct.unpack("!Q", self._recv_exact(8))[0]
            payload = self._recv_exact(ln)
            if opcode == 8: raise ConnectionError("ws closed")
            try: m = json.loads(payload.decode())
            except Exception: continue
            if want_id is not None and m.get("id") == want_id: return m
            msgs.append(m)
        return None
    def call(self, method, params=None, timeout=30):
        mid = self.send(method, params)
        while True:
            m = self.recv_msg(want_id=mid, timeout=timeout)
            if m is None: raise TimeoutError(method)
            if "error" in m: raise RuntimeError(f"{method}: {m['error']}")
            return m.get("result", {})

# find the gmc tab
tabs = json.loads(urllib.request.urlopen(f"{CDP}/json/list", timeout=10).read())
page = next(t for t in tabs if t.get("type") == "page" and "gmc.darvis" in t.get("url", ""))
print("target tab:", page["title"][:50])
c = CDPClient(page["webSocketDebuggerUrl"])

# ── device emulation: iPhone-ish 390x844 DPR2 touch ──
c.call("Emulation.setDeviceMetricsOverride",
       {"width": 390, "height": 844, "deviceScaleFactor": 2, "mobile": True})
c.call("Emulation.setTouchEmulationEnabled", {"enabled": True, "maxTouchPoints": 5})
c.call("Emulation.setUserAgentOverride",
       {"userAgent": "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1"})
print("device mode: 390x844 dpr2 mobile")

import os
os.makedirs(OUT, exist_ok=True)

def shot(name):
    r = c.call("Page.captureScreenshot", {"format": "png"}, timeout=30)
    path = f"{OUT}/{name}.png"
    open(path, "wb").write(base64.b64decode(r["data"]))
    print("shot:", path)
    return path

def js(expr, timeout=30):
    r = c.call("Runtime.evaluate", {"expression": expr, "returnByValue": True, "awaitPromise": True}, timeout=timeout)
    return r.get("result", {}).get("value")

c.call("Page.enable")
c.call("Page.navigate", {"url": SITE + "/login"})
time.sleep(3.5)

# fonts check (real Chrome should render them)
fonts = js("(async()=>{await document.fonts.ready;return JSON.stringify({cabinet:document.fonts.check('16px \"Cabinet Grotesk\"'),inter:document.fonts.check('16px Inter'),mono:document.fonts.check('16px \"JetBrains Mono\"')})})()", 20)
print("webfonts:", fonts)

# login
js("(async()=>{const c=[...document.querySelectorAll('button')].find(b=>b.textContent.trim()==='Sales');if(c)c.click();await new Promise(r=>setTimeout(r,400));const s=[...document.querySelectorAll('button')].find(b=>b.textContent.trim()==='Sign in');if(s)s.click();return 'in'})()")
time.sleep(3)

AUDIT = r"""
(function(){
  const vw = innerWidth, out = {vw, pageOverflow: document.documentElement.scrollWidth > vw + 1,
    overflows: [], kpiCols: null, tables: [], tiny: [], fontsRendered: getComputedStyle(document.body).fontFamily.slice(0,40)};
  document.querySelectorAll('.ox-content *').forEach(el => {
    const r = el.getBoundingClientRect();
    if (r.width === 0) return;
    const wrap = el.closest('.ox-table-wrap');
    if (wrap && wrap !== el) return;
    if (r.right > vw + 1) out.overflows.push((el.className||el.tagName).toString().slice(0,30)+' r='+Math.round(r.right));
  });
  out.overflows = [...new Set(out.overflows)].slice(0, 6);
  const kpi = document.querySelector('.ox-kpi-row');
  if (kpi) out.kpiCols = getComputedStyle(kpi).gridTemplateColumns.split(' ').length;
  document.querySelectorAll('table.ox-table').forEach(t => {
    const tr = t.querySelector('tbody tr');
    if (tr) out.tables.push(getComputedStyle(tr).display === 'block' ? 'stacked' : 'h-scroll');
  });
  return JSON.stringify(out);
})()
"""

ROUTES = [("#/", "01-cases"), ("#/case/30", "02-case-detail"), ("#/exceptions", "03-exceptions"),
          ("#/users", "04-users"), ("#/admin", "05-admin"), ("#/about", "06-about")]
results = {}
for route, name in ROUTES:
    c.call("Page.navigate", {"url": SITE + "/" + route})
    time.sleep(2.8)
    results[name] = js(AUDIT)
    print(name, "→", (results[name] or "")[:260])
    shot(name)

# drawer on mobile
c.call("Page.navigate", {"url": SITE + "/#/"})
time.sleep(2.5)
js("document.getElementById('new-case')?.click();'1'")
time.sleep(1.2)
drawer = js("(function(){const d=document.getElementById('drawer');const r=d.getBoundingClientRect();return JSON.stringify({x:Math.round(r.x),w:Math.round(r.width),vw:innerWidth,fits:r.x>=0&&r.right<=innerWidth+1,open:d.classList.contains('open')})})()")
print("drawer:", drawer)
shot("07-drawer")

# burger + sidebar
js("closeDrawer?.();document.getElementById('drawer-backdrop')?.click();'c'")
time.sleep(0.8)
burger = js("(function(){const b=document.querySelector('.ox-burger');if(!b)return 'none';const cs=getComputedStyle(b);return JSON.stringify({display:cs.display,w:b.getBoundingClientRect().width})})()")
print("burger:", burger)
js("document.getElementById('burger')?.click();'b'")
time.sleep(0.8)
sb = js("(function(){const s=document.getElementById('sidebar');const r=s.getBoundingClientRect();return JSON.stringify({open:s.classList.contains('ox-sidebar-open'),x:Math.round(r.x),w:Math.round(r.width)})})()")
print("sidebar:", sb)
shot("08-nav-open")

print("\n==== RESULTS ====")
for k, v in results.items(): print(k, v)
print("drawer", drawer, "| burger", burger, "| sidebar", sb)
