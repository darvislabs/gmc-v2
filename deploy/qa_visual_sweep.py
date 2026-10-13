"""Full visual QA sweep of gmc.darvis.si via camofox HTTP API.
Every page, desktop 1920x1080 + mobile 390x844, drawer open/close, viewport checks."""
import json, urllib.request, urllib.error, time, base64, sys

BASE = "http://localhost:9377"
SITE = "https://gmc.darvis.si"
OUT = "/home/ubuntu/gmc-v2-deploy/qa-shots"
USER = "qa-bot"
TID = None

def req(method, path, data=None, timeout=120, raw=False):
    body = json.dumps(data).encode() if data is not None else None
    r = urllib.request.Request(BASE + path, data=body, headers={"Content-Type": "application/json"}, method=method)
    try:
        with urllib.request.urlopen(r, timeout=timeout) as resp:
            payload = resp.read()
            if raw:
                return {"_raw": payload, "ctype": resp.headers.get("Content-Type", "")}
            return json.loads(payload.decode() or "{}")
    except urllib.error.HTTPError as e:
        return {"HTTP_ERROR": e.code, "body": e.read().decode()[:300]}

def open_tab(url):
    global TID
    r = req("POST", "/tabs/open", {"url": url, "userId": USER})
    TID = r.get("tabId") or r.get("targetId")
    return r

def navigate(url): return req("POST", f"/tabs/{TID}/navigate", {"url": url, "userId": USER})
def viewport(w, h): return req("POST", f"/tabs/{TID}/viewport", {"width": w, "height": h, "userId": USER})
def snapshot(): return req("GET", f"/tabs/{TID}/snapshot?userId={USER}")
def click_ref(ref): return req("POST", f"/tabs/{TID}/click", {"ref": ref, "userId": USER})
def evaluate(expr): return req("POST", f"/tabs/{TID}/evaluate", {"expression": expr, "userId": USER})
def screenshot():
    r = req("GET", f"/tabs/{TID}/screenshot?userId={USER}", timeout=180, raw=True)
    return r

def find_ref(snap, needle):
    """walk snapshot JSON for a ref whose text/label contains needle (ci)"""
    out = []
    def walk(o):
        if isinstance(o, dict):
            txt = json.dumps({k: v for k, v in o.items() if k in ("text", "label", "name", "value")}, ensure_ascii=False).lower()
            if "ref" in o and needle.lower() in txt:
                out.append(o["ref"])
            for v in o.values(): walk(v)
        elif isinstance(o, list):
            for v in o: walk(v)
    walk(snap)
    return out[0] if out else None

report = []
def check(name, ok, detail=""):
    report.append((name, ok, detail))
    print(("PASS " if ok else "FAIL ") + name + (" — " + detail if detail else ""))

def save_shot(name):
    r = screenshot()
    if "_raw" not in r:
        check(f"shot:{name}", False, str(r)); return None
    ctype = r.get("ctype", "")
    payload = r["_raw"]
    path = f"{OUT}/{name}.png"
    try:
        if "json" in ctype:
            j = json.loads(payload.decode())
            b64 = j.get("base64") or j.get("image") or j.get("data")
            if not b64:
                check(f"shot:{name}", False, json.dumps(j)[:150]); return None
            payload = base64.b64decode(b64)
        open(path, "wb").write(payload)
        check(f"shot:{name}", True, path)
        return path
    except Exception as e:
        check(f"shot:{name}", False, repr(e)[:120]); return None

# ── login ────────────────────────────────────────────────────────────────────
open_tab(SITE + "/login")
viewport(1920, 1080)
time.sleep(2)
snap = snapshot()
ref = find_ref(snap, "Sales")
if ref: click_ref(ref)
time.sleep(0.5)
ref = find_ref(snap, "Sign in")
click_ref(ref) if ref else evaluate("document.querySelector('.ox-login-btn')?.click()")
time.sleep(2.5)
who = evaluate("JSON.stringify({u:document.body.innerText.includes('Sales'),hash:location.hash})")
check("login as Sales", "true" in str(who).lower() or "sales" in str(who).lower(), str(who)[:120])

# ── DESKTOP 1920 ─────────────────────────────────────────────────────────────
ROUTES = [("#/", "cases"), ("#/exceptions", "exceptions"), ("#/users", "users"),
          ("#/admin", "admin"), ("#/about", "about"), ("#/case/8", "case-detail")]
for route, name in ROUTES:
    navigate(SITE + "/" + route)
    time.sleep(2.2)
    save_shot(f"desktop-{name}")
    kpis = evaluate("JSON.stringify({kpiCount:document.querySelectorAll('.ox-kpi').length,"
                    "hasVhScroll:document.documentElement.scrollHeight>document.documentElement.clientHeight+2,"
                    "hOverflow:document.documentElement.scrollWidth>document.documentElement.clientWidth+2})")
    check(f"desktop/{name} no h-overflow", "hOverflow\\\":false" in json.dumps(kpis) or "\"hOverflow\":false" in str(kpis), str(kpis)[:140])

# drawer on desktop
navigate(SITE + "/#/")
time.sleep(2)
evaluate("document.getElementById('new-case')?.click();'1'")
time.sleep(1.2)
save_shot("desktop-drawer-open")
st = evaluate("const d=document.getElementById('drawer'),b=document.getElementById('drawer-backdrop'),r=d.getBoundingClientRect();"
              "JSON.stringify({x:Math.round(r.x),w:Math.round(r.width),vw:innerWidth,open:d.classList.contains('open'),"
              "drawerZ:getComputedStyle(d).zIndex,backdropZ:getComputedStyle(b).zIndex,"
              "inputCount:d.querySelectorAll('input,select').length,btnText:document.getElementById('nc-submit')?.textContent?.trim()})")
check("desktop drawer on-screen + above backdrop", '"open":true' in str(st) and '"drawerZ":"100"' in str(st) and '"backdropZ":"90"' in str(st), str(st)[:200])

# ── MOBILE 390x844 ───────────────────────────────────────────────────────────
viewport(390, 844)
for route, name in [("#/", "cases"), ("#/case/8", "case-detail"), ("#/admin", "admin"), ("#/exceptions", "exceptions")]:
    navigate(SITE + "/" + route)
    time.sleep(2.2)
    save_shot(f"mobile-{name}")
    ov = evaluate("JSON.stringify({hOverflow:document.documentElement.scrollWidth>document.documentElement.clientWidth+2,sw:document.documentElement.scrollWidth,vw:document.documentElement.clientWidth})")
    check(f"mobile/{name} no h-overflow", '"hOverflow":false' in str(ov) or 'hOverflow\\":false' in json.dumps(ov), str(ov)[:140])

# burger nav check
navigate(SITE + "/#/")
time.sleep(2)
burger = evaluate("JSON.stringify({burgerVisible:!!document.querySelector('.ox-burger')&&getComputedStyle(document.querySelector('.ox-burger')).display!=='none'})")
check("mobile burger visible", '"burgerVisible":true' in str(burger), str(burger)[:120])

# drawer on mobile
evaluate("document.getElementById('new-case')?.click();'1'")
time.sleep(1.2)
save_shot("mobile-drawer-open")
st = evaluate("const d=document.getElementById('drawer'),r=d.getBoundingClientRect();"
              "JSON.stringify({x:Math.round(r.x),w:Math.round(r.width),vw:innerWidth,open:d.classList.contains('open'),"
              "fitsFull:r.x>=0&&r.right<=innerWidth+1})")
check("mobile drawer full-width & on-screen", '"open":true' in str(st) and '"fitsFull":true' in str(st), str(st)[:200])

print("\n===== SUMMARY =====")
fails = [r for r in report if not r[1]]
print(f"{len(report)-len(fails)}/{len(report)} checks passed")
for n, _, d in fails: print("FAILED:", n, "—", d[:160])
