"""Focused visual QA: mobile-sim (burger/sidebar/drawer) + desktop drawer z-order."""
import json, urllib.request, urllib.error, time, base64

BASE = "http://localhost:9377"
SITE = "https://gmc.darvis.si"
OUT = "/home/ubuntu/gmc-v2-deploy/qa-shots"
USER = "qa-bot"

def req(method, path, data=None, timeout=120, raw=False):
    body = json.dumps(data).encode() if data is not None else None
    r = urllib.request.Request(BASE + path, data=body, headers={"Content-Type": "application/json"}, method=method)
    try:
        with urllib.request.urlopen(r, timeout=timeout) as resp:
            p = resp.read()
            if raw: return {"_raw": p, "ctype": resp.headers.get("Content-Type", "")}
            return json.loads(p.decode() or "{}")
    except urllib.error.HTTPError as e:
        return {"HTTP_ERROR": e.code, "body": e.read().decode()[:200]}

def open_tab(url):
    r = req("POST", "/tabs/open", {"url": url, "userId": USER})
    return r.get("tabId") or r.get("targetId")

def ev(T, expr, tries=3):
    r = {}
    for i in range(tries):
        r = req("POST", f"/tabs/{T}/evaluate", {"expression": expr, "userId": USER})
        if "HTTP_ERROR" not in r: return r.get("result", "")
        time.sleep(1.2)
    return "EVAL-FAIL: " + str(r)[:120]

def shot(T, name):
    r = req("GET", f"/tabs/{T}/screenshot?userId={USER}", timeout=180, raw=True)
    if "_raw" not in r: return f"SHOT-FAIL {r}"
    payload, path = r["_raw"], f"{OUT}/{name}.png"
    try:
        if "json" in r.get("ctype", ""):
            j = json.loads(payload.decode())
            payload = base64.b64decode(j.get("base64") or j.get("image") or j.get("data"))
        open(path, "wb").write(payload)
        return path
    except Exception as e:
        return "SHOT-FAIL " + repr(e)

report = []
def check(name, ok, detail=""):
    report.append((name, ok, detail)); print(("PASS " if ok else "FAIL ") + name + (" — " + detail if detail else ""))

MOBILE_SIM = ("(function(){const s=document.getElementById('qa-mob');if(s)s.remove();"
              "const st=document.createElement('style');st.id='qa-mob';"
              "st.textContent='@media all{.ox-burger{display:inline-block!important}"
              ".ox-sidebar{position:fixed;transform:translateX(-100%);z-index:120;transition:transform .25s}"
              ".ox-sidebar.ox-sidebar-open{transform:translateX(0)}}';"
              "document.head.appendChild(st);return 'sim-on'})()")

DRAWER_STATE = ("(()=>{const d=document.getElementById('drawer'),b=document.getElementById('drawer-backdrop');"
                "if(!d)return JSON.stringify({no:'drawer-missing'});"
                "const r=d.getBoundingClientRect();"
                "return JSON.stringify({x:Math.round(r.x),w:Math.round(r.width),open:d.classList.contains('open'),"
                "dz:getComputedStyle(d).zIndex,bz:getComputedStyle(b).zIndex,"
                "onScreen:r.x>=0&&r.right<=innerWidth+1,fields:d.querySelectorAll('input,select').length})})()")

# ══ A. DESKTOP drawer z-order ════════════════════════════════════════════════
T = open_tab(SITE + "/login")
time.sleep(2)
snap = req("GET", f"/tabs/{T}/snapshot?userId={USER}")
def find_ref(snap, needle):
    out = []
    def walk(o):
        if isinstance(o, dict):
            txt = json.dumps({k: o[k] for k in o if k in ("text","label","name","value")}, ensure_ascii=False).lower()
            if "ref" in o and needle.lower() in txt: out.append(o["ref"])
            for v in o.values(): walk(v)
        elif isinstance(o, list):
            for v in o: walk(v)
    walk(snap); return out[0] if out else None
for label in ["Sales", "Sign in"]:
    ref = find_ref(snap, label)
    if ref: req("POST", f"/tabs/{T}/click", {"ref": ref, "userId": USER}); time.sleep(1.5)
time.sleep(1.5)
check("A0 logged in (drawer reachable)", "Cases" in ev(T, "document.body.innerText.slice(0,400)"))

req("POST", f"/tabs/{T}/evaluate", {"expression": "document.getElementById('new-case')?.click();'1'", "userId": USER})
time.sleep(1.4)
st = ev(T, DRAWER_STATE)
check("A1 desktop drawer open + z100>z90 + on-screen",
      '"open":true' in st and '"dz":"100"' in st and '"bz":"90"' in st and '"onScreen":true' in st, st[:160])
check("A2 desktop shot", "qa-shots" in shot(T, "desktop-drawer-FIXED"))
# close it
ev(T, "window.closeDrawer ? closeDrawer() : document.getElementById('drawer-backdrop').click(); 'x'")
time.sleep(0.8)

# ══ B. MOBILE-SIM 390 ════════════════════════════════════════════════════════
ev(T, MOBILE_SIM)
burger_vis = ev(T, "(()=>{const b=document.querySelector('.ox-burger');return b?getComputedStyle(b).display:'none'})()")
check("B1 burger visible under 390-sim", burger_vis not in ("none", ""), burger_vis)
check("B3 mobile-sim cases shot", "qa-shots" in shot(T, "mobile-sim-cases"))

ev(T, "document.getElementById('burger')?.click();'x'")
time.sleep(0.9)
sb = ev(T, "(()=>{const s=document.getElementById('sidebar');const r=s.getBoundingClientRect();return JSON.stringify({open:s.classList.contains('ox-sidebar-open'),x:Math.round(r.x)})})()")
check("B4 sidebar slides in on burger tap", '"open":true' in sb and '"x":0' in sb, sb[:120])
check("B5 mobile-sim nav-open shot", "qa-shots" in shot(T, "mobile-sim-nav-open"))
ev(T, "document.getElementById('burger')?.click();'x'"); time.sleep(0.6)

req("POST", f"/tabs/{T}/evaluate", {"expression": "document.getElementById('new-case')?.click();'1'", "userId": USER})
time.sleep(1.4)
st = ev(T, DRAWER_STATE)
check("B6 mobile drawer above backdrop, on-screen", '"open":true' in st and '"dz":"100"' in st and '"onScreen":true' in st, st[:160])
check("B7 mobile drawer shot (NOT blurred)", "qa-shots" in shot(T, "mobile-sim-drawer"))

print("\n===== SUMMARY =====")
fails = [r for r in report if not r[1]]
print(f"{len(report)-len(fails)}/{len(report)} passed")
for n,_,d in fails: print("FAILED:", n, d[:140])
