import json, urllib.request, urllib.error, http.cookiejar

JAR = http.cookiejar.CookieJar()
OP = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(JAR))

def call(method, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request("http://127.0.0.1:5063" + path, data, {"Content-Type": "application/json"}, method=method)
    try:
        with OP.open(req, timeout=120) as r:
            return r.status, r.read().decode()
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()

call("POST", "/api/v1/auth/login", {"email": "sales@opexminds.com", "password": "Sales@12345"})
code, body = call("POST", "/api/v1/cases", {"clientName": "UI Acceptance", "businessType": "Rollover"})
cid = json.loads(body)["caseId"]

# the demo dataset headers the CodeClinical stage actually recognizes
docs = [
    ("ClaimsRegister", "claims.csv", [["Claim No", "Member No", "Claim Status", "Claimed Amount", "Paid Amount", "Sanctioned Amt", "Admission Date", "Discharge Date", "Disease"]] + [["C%05d" % i, "M%05d" % (i % 1240), "Paid" if i % 9 else "Outstanding", str(18000 + (i * 137) % 90000), str(17000 + (i * 131) % 85000), str(17500 + (i * 133) % 87000), "2025-%02d-%02d" % (1 + i % 12, 1 + i % 27), "2025-%02d-%02d" % (1 + i % 12, 3 + i % 25), ["Viral fever","Diabetes Mellitus","Hypertension","Maternity - Normal","Cataract","Fracture femur"][i % 6]] for i in range(1, 181)]),
    ("Enrollment", "enrollment.csv", [["Member No", "Employee Code", "Member Name", "Relationship", "Gender", "DOB", "City"]] + [["M%05d" % i, "E%05d" % (i if i <= 825 else i - 825), "Member %d" % i, ["Employee","Spouse","Child","Parent"][i % 4], "M" if i % 2 else "F", "%04d-%02d-%02d" % (1955 + i % 45, 1 + i % 12, 1 + i % 27), ["Ahmedabad","Mumbai","Pune"][i % 3]] for i in range(1, 251)]),
    ("ExpiringPolicy", "expiring.txt", [["Benefit", "Value"], ["BaseSumInsured", "300000"], ["MaternityNormal", "65000"], ["MaternityCs", "95000"], ["Ambulance", "1500"], ["Newborn", "0"], ["RoomRent", "1%"], ["PedDay1", "No"]]),
    ("Rfq", "rfq.txt", [["Benefit", "Value"], ["BaseSumInsured", "500000"], ["MaternityNormal", "75000"], ["MaternityCs", "100000"], ["Ambulance", "3000"], ["Newborn", "Day 1"], ["RoomRent", "2%"], ["PedDay1", "Yes"]]),
]
for dt, fn, rr in docs:
    code, body = call("POST", f"/api/v1/cases/{cid}/documents", {"fileName": fn, "docType": dt, "rows": rr})
    assert code == 200, f"{dt}: {code} {body[:120]}"
print("documents uploaded")

code, body = call("POST", f"/api/v1/cases/{cid}/pipeline/start")
d = json.loads(body)
print("status:", d["status"], "| stages:", d["stageCount"])

code, body = call("GET", f"/api/v1/cases/{cid}/quotes")
qs = json.loads(body)
if qs:
    p = qs[0]["pricing"]
    comps = p["components"]
    tech = [c for c in comps if "technical premium" in c["componentName"].lower()][-1]["value"]
    gst = [c for c in comps if c["componentName"].lower().startswith("gst")][-1]["value"]
    total = [c for c in comps if "total premium" in c["componentName"].lower()][-1]["value"]
    print(f"tech: {tech:,.0f} | GST: {gst:,.0f} | total: {total:,.0f} | bridge: {len(comps)} rows")
    print("guardrails:", qs[0]["guardrails"]["outcome"], "conf", qs[0]["guardrails"]["confidence"])
else:
    print("NO QUOTE")

# role checks
print(call("POST", "/api/v1/auth/login", {"email": "uw@opexminds.com", "password": "Uw@12345"})[0], "uw login")
code, body = call("POST", "/api/v1/uw/authority/check", {"deviationPct": 7.9})
print("authority 7.9%:", json.loads(body))
