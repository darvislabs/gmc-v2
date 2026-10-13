"""End-to-end: REAL file uploads (xlsx + csv) through the UI upload endpoint → pipeline → quote."""
import csv, io, json, urllib.request, urllib.error, http.cookiejar

BASE = "http://127.0.0.1:5063"
JAR = http.cookiejar.CookieJar()
OP = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(JAR))

def call(method, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data, {"Content-Type": "application/json"}, method=method)
    try:
        with OP.open(req, timeout=180) as r: return r.status, r.read().decode()
    except urllib.error.HTTPError as e: return e.code, e.read().decode()

def upload(path, filename, blob, doc_type):
    boundary = "----GmcV2Probe7d9f"
    body = io.BytesIO()
    body.write(f"--{boundary}\r\n".encode())
    body.write(f'Content-Disposition: form-data; name="docType"\r\n\r\n{doc_type}\r\n'.encode())
    body.write(f"--{boundary}\r\n".encode())
    body.write(f'Content-Disposition: form-data; name="file"; filename="{filename}"\r\n'.encode())
    body.write(b"Content-Type: application/octet-stream\r\n\r\n")
    body.write(blob)
    body.write(f"\r\n--{boundary}--\r\n".encode())
    req = urllib.request.Request(BASE + path, body.getvalue(),
        {"Content-Type": f"multipart/form-data; boundary={boundary}"}, method="POST")
    try:
        with OP.open(req, timeout=120) as r: return r.status, json.loads(r.read().decode())
    except urllib.error.HTTPError as e: return e.code, e.read().decode()

print(call("POST", "/api/v1/auth/login", {"email": "sales@opexminds.com", "password": "Sales@12345"})[0], "login")
code, body = call("POST", "/api/v1/cases", {"clientName": "XYZ Real Files Pvt Ltd", "businessType": "Rollover"})
cid = json.loads(body)["caseId"]
print(f"case {cid}")

# ── enrollment as real xlsx (built with closedxml-equivalent: raw xlsx via python openpyxl if present, else csv) ──
try:
    from openpyxl import Workbook
    wb = Workbook()
    ws = wb.active; ws.title = "Enrollment"
    ws.append(["Member No", "Relationship", "Gender", "Age", "Chronic Flag"])
    for i in range(1, 121):
        rel = ["Employee", "Spouse", "Child", "Parent"][i % 4]
        ws.append([f"M{i:05d}", rel, "M" if i % 2 else "F", 25 + (i * 7) % 45, "1" if i % 6 == 0 else "0"])
    buf = io.BytesIO(); wb.save(buf)
    code, res = upload(f"/api/v1/cases/{cid}/documents/upload", "enrollment.xlsx", buf.getvalue(), "Enrollment")
    print("xlsx enrollment →", res.get("parseStatus"), f"{res.get('rows')} rows, {res.get('sheets')} sheet(s)")
except ImportError:
    print("openpyxl not available — csv fallback")
    si = io.StringIO()
    w = csv.writer(si)
    w.writerow(["Member No", "Relationship", "Gender", "Age", "Chronic Flag"])
    for i in range(1, 121):
        w.writerow([f"M{i:05d}", ["Employee", "Spouse", "Child", "Parent"][i % 4], "M" if i % 2 else "F", 25 + (i * 7) % 45, "1" if i % 6 == 0 else "0"])
    code, res = upload(f"/api/v1/cases/{cid}/documents/upload", "enrollment.csv", si.getvalue().encode(), "Enrollment")
    print("csv enrollment →", res.get("parseStatus"), f"{res.get('rows')} rows")

# ── claims with quoted commas (RFC4180 torture) ──
si = io.StringIO()
w = csv.writer(si)
w.writerow(["Claim No", "Member No", "Claim Status", "Sanctioned Amt", "Disease"])
diseases = ["Diabetes Mellitus", "Hypertension", "Normal Delivery", "Cataract", "Pneumonia", "Fracture, femur (compound)"]
for i in range(1, 301):
    w.writerow([f"C{i:05d}", f"M{(i % 120) + 1:05d}", "Paid" if i % 8 else "Outstanding", 12000 + (i * 211) % 250000, diseases[i % 6]])
code, res = upload(f"/api/v1/cases/{cid}/documents/upload", "claims.csv", si.getvalue().encode(), "ClaimsRegister")
print("csv claims (quoted commas) →", res.get("parseStatus"), f"{res.get('rows')} rows")

# ── expiring + rfq as benefit/value text files ──
exp = "Benefit,Value\nBaseSumInsured,300000\nMaternityNormal,50000\nMaternityCs,75000\nAmbulance,1500\nRoomRent,1%\nPedDay1,No\n"
code, res = upload(f"/api/v1/cases/{cid}/documents/upload", "expiring.txt", exp.encode(), "ExpiringPolicy")
print("txt expiring →", res.get("parseStatus"), f"{res.get('rows')} rows")
rfq = "Benefit,Value\nBaseSumInsured,500000\nMaternityNormal,75000\nMaternityCs,100000\nAmbulance,3000\nRoomRent,2%\nPedDay1,Yes\n"
code, res = upload(f"/api/v1/cases/{cid}/documents/upload", "rfq.txt", rfq.encode(), "Rfq")
print("txt rfq →", res.get("parseStatus"), f"{res.get('rows')} rows")

# ── run pipeline over the REAL data ──
code, body = call("POST", f"/api/v1/cases/{cid}/pipeline/start")
d = json.loads(body)
print("pipeline →", d["status"], "| stages:", d["stageCount"], "| dataSource:", d.get("dataSource"))
code, body = call("GET", f"/api/v1/cases/{cid}/quotes")
qs = json.loads(body)
if qs:
    p = qs[0]["pricing"]; comps = p["components"]
    tech = [c for c in comps if "technical premium" in c["componentName"].lower()][-1]["value"]
    lives = qs[0]["analytics"]["coveredLives"] if qs[0].get("analytics") else "?"
    print(f"quote over REAL data → tech ₹{tech:,.0f} | covered lives: {lives}")
else:
    print("NO QUOTE —", [e.get("code") for e in call("GET", "/api/v1/exceptions")[1] and []] or "check exceptions")
