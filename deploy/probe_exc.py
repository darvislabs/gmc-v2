import json, urllib.request, http.cookiejar

JAR = http.cookiejar.CookieJar()
OP = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(JAR))

def call(method, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request("http://127.0.0.1:5063" + path, data, {"Content-Type": "application/json"}, method=method)
    try:
        with OP.open(req, timeout=60) as r:
            return r.status, r.read().decode()
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()

print(call("POST", "/api/v1/auth/login", {"email": "sales@opexminds.com", "password": "Sales@12345"})[0])
code, body = call("POST", "/api/v1/cases", {"clientName": "Exc Probe", "businessType": "Rollover"})
cid = json.loads(body)["caseId"]
for dt in ["ClaimsRegister", "Enrollment", "ExpiringPolicy", "Rfq"]:
    call("POST", f"/api/v1/cases/{cid}/documents", {"fileName": f"{dt}.csv", "docType": dt, "rows": [["h1"], ["v1"]]})
code, body = call("POST", f"/api/v1/cases/{cid}/pipeline/start")
d = json.loads(body)
print("status:", d["status"], "stages:", d["stageCount"])
for s in d["stages"]:
    print(f"  {s['stage']:<20} {s['state']}")
code, body = call("GET", "/api/v1/exceptions")
for e in json.loads(body):
    if e.get("caseId") == cid:
        print("EXC:", e.get("code"), e.get("severity"), "|", str(e.get("message"))[:100])
