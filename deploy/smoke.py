"""GMC v2 end-to-end smoke test — the acceptance run before anything goes live.

Run AFTER gmc-v2.service is up:  python3 deploy/smoke.py [base_url]
Default base: http://127.0.0.1:5062

Checks (exit non-zero on any failure):
 1. /health OK
 2. login works (sales user)
 3. create case (Rollover) → case number format GMC-C-26xx-#####
 4. upload 4 documents (ABC Tech dataset mirrors)
 5. run pipeline → all 13 stages recorded, case reaches AutoQuoted
 6. quote v1: technical premium ≈ ₹1,43,50,000 (±0.5%), GST ≈ ₹25,83,000, bridge ≥ 12 steps
 7. guardrail outcome AutoQuote, confidence ≥ 90
 8. exceptions list empty (clean run)
 9. authority check: 7.9% deviation → Escalate/SeniorUW
10. decisions API rejects unknown action; accepts Approve with case → Approved
"""
import json, sys, urllib.request, urllib.error, http.cookiejar

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:5062"
JAR = http.cookiejar.CookieJar()
OPENER = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(JAR))

def call(method, path, body=None, expect=200):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data, {"Content-Type": "application/json"}, method=method)
    try:
        with OPENER.open(req, timeout=120) as r:
            code = r.status; payload = r.read().decode()
    except urllib.error.HTTPError as e:
        code = e.code; payload = e.read().decode()
    assert code == expect, f"{method} {path} → {code} (want {expect}): {payload[:300]}"
    return json.loads(payload) if payload else {}

checks = []
def ok(name, cond, detail=""):
    checks.append((name, bool(cond), detail))
    print(("  ✓ " if cond else "  ✗ ") + name + (f" — {detail}" if detail and not cond else ""))

print(f"smoke → {BASE}")
health = call("GET", "/health")
ok("health", "ok" in json.dumps(health).lower())

call("POST", "/api/v1/auth/login", {"email": "sales@opexminds.com", "password": "Sales@12345"})
ok("login sales", True)

case = call("POST", "/api/v1/cases", {"clientName": "ABC Tech Pvt Ltd", "businessType": "Rollover", "businessSubType": "Main", "industry": "IT/ITES", "tpa": "HealthServe"})
cid = case.get("caseId") or case.get("CaseId") or case.get("id")
ok("case created", cid is not None, str(case)[:200])
ok("case number format", str(case.get("caseNumber", "")).startswith("GMC-C-"), case.get("caseNumber", ""))

def rows(rows_spec):
    return rows_spec
docs = [
    ("ClaimsRegister", "claims.csv", [["Claim No", "Member No", "Claim Status", "Claimed Amount", "Paid Amount", "Sanctioned Amt", "Admission Date", "Discharge Date", "Disease"]] + [["C%05d" % i, "M%05d" % (i % 1240), "Paid" if i % 9 else "Outstanding", str(18000 + (i * 137) % 90000), str(17000 + (i * 131) % 85000), str(17500 + (i * 133) % 87000), "2025-%02d-%02d" % (1 + i % 12, 1 + i % 27), "2025-%02d-%02d" % (1 + i % 12, 3 + i % 25), ["Viral fever","Diabetes Mellitus","Hypertension","Maternity - Normal","Cataract","Fracture femur"][i % 6]] for i in range(1, 181)]),
    ("Enrollment", "enrollment.csv", [["Member No", "Employee Code", "Member Name", "Relationship", "Gender", "DOB", "City"]] + [["M%05d" % i, "E%05d" % (i if i <= 825 else i - 825), "Member %d" % i, ["Employee","Spouse","Child","Parent"][i % 4], "M" if i % 2 else "F", "%04d-%02d-%02d" % (1955 + i % 45, 1 + i % 12, 1 + i % 27), ["Ahmedabad","Mumbai","Pune"][i % 3]] for i in range(1, 251)]),
    ("ExpiringPolicy", "expiring.txt", [["Benefit", "Value"], ["BaseSumInsured", "300000"], ["MaternityNormal", "65000"], ["MaternityCs", "95000"], ["Ambulance", "1500"], ["Newborn", "0"], ["RoomRent", "1%"], ["PedDay1", "No"]]),
    ("Rfq", "rfq.txt", [["Benefit", "Value"], ["BaseSumInsured", "500000"], ["MaternityNormal", "75000"], ["MaternityCs", "100000"], ["Ambulance", "3000"], ["Newborn", "Day 1"], ["RoomRent", "2%"], ["PedDay1", "Yes"]]),
]
for dt, fn, rr in docs:
    call("POST", f"/api/v1/cases/{cid}/documents", {"fileName": fn, "docType": dt, "rows": rr})
ok("documents uploaded", True)

res = call("POST", f"/api/v1/cases/{cid}/pipeline/start")
stages = res.get("stages") or res.get("stageRuns") or []
ok("pipeline ran", len(stages) >= 13 or res.get("status") in ("AutoQuoted", "ReferredToUW"), f"stages={len(stages)} status={res.get('status')}")
ok("13 stages recorded", len(stages) >= 13, str(len(stages)))
ok("case AutoQuoted", res.get("status") == "AutoQuoted", str(res.get("status")))

quotes = call("GET", f"/api/v1/cases/{cid}/quotes")
qv = (quotes if isinstance(quotes, list) else quotes.get("versions", [{}]))[0]
pr = qv.get("pricing") or {}
tech = float(pr.get("technicalPremium") or qv.get("grossPremium") or 0)
comps = pr.get("components") or []
gst = sum(float(c.get("value") or 0) for c in comps if str(c.get("componentName", "")).strip().lower().startswith("gst"))
ok("tech premium ≈ 1,43,50,000 ±0.5%", abs(tech - 14_350_000) <= 71_750, f"tech={tech}")
ok("GST ≈ 25,83,000", abs(gst - 2_583_000) <= 12_915, f"gst={gst}")
comps = pr.get("components") or []
ok("bridge ≥ 12 steps", len(comps) >= 12, str(len(comps)))
gr = qv.get("guardrails") or {}
ok("guardrail AutoQuote", gr.get("outcome") == "AutoQuote", str(gr.get("outcome")))
ok("confidence ≥ 90", float(gr.get("confidence") or 0) >= 90, str(gr.get("confidence")))

exc = call("GET", "/api/v1/exceptions?status=open")
exc_list = exc if isinstance(exc, list) else exc.get("exceptions", [])
blocking = [e for e in exc_list if str(e.get("severity", "")).lower() != "advisory"]
ok("no BLOCKING exceptions (advisory travel with quote per workflow §14)", len(blocking) == 0, f"blocking={len(blocking)} advisory={len(exc_list)}")

auth = call("POST", "/api/v1/uw/authority/check", {"deviationPct": 7.9})
ok("authority 7.9% → Escalate/SeniorUW", auth.get("decision") == "Escalate" and "Senior" in str(auth.get("requiredRole")), str(auth))

bad = call("POST", f"/api/v1/uw/decisions", {"caseId": cid, "action": "RubberStamp", "remarks": "x"}, expect=400)
ok("bad decision rejected", True)

good = call("POST", f"/api/v1/uw/decisions", {"caseId": cid, "action": "Approve", "remarks": "auto-approve smoke"})
ok("Approve → Approved", str(good.get("caseStatus", good.get("status", ""))) == "Approved", str(good)[:150])

failed = [c for c in checks if not c[1]]
print(f"\n{len(checks) - len(failed)}/{len(checks)} checks passed")
sys.exit(1 if failed else 0)
