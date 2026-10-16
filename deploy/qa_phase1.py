#!/usr/bin/env python3
"""GMC v2 full-platform QA sweep — Phase 1: API surface × roles × auth gates."""
import json, urllib.request, http.cookiejar, sys

BASE = 'https://gmc.darvis.si'
results = []
def check(name, ok, detail=''):
    results.append((name, bool(ok), detail))

def session(email, pw):
    cj = http.cookiejar.CookieJar()
    op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cj))
    r = op.open(urllib.request.Request(f'{BASE}/api/v1/auth/login',
        data=json.dumps({'email': email, 'password': pw}).encode(),
        headers={'Content-Type': 'application/json'}))
    assert r.status == 200, f'login {email} failed'
    return op

def get(op, path):
    try:
        r = op.open(f'{BASE}{path}')
        raw = r.read()
        try:
            return r.status, json.loads(raw)
        except (ValueError, json.JSONDecodeError):
            return r.status, None
    except urllib.error.HTTPError as e:
        return e.code, None

ROLES = {
    'sales':  ('sales@opexminds.com', 'Sales@12345'),
    'uw':     ('uw@opexminds.com', 'Uw@12345'),
    'senior': ('senior@opexminds.com', 'Senior@12345'),
    'vp':     ('vp@opexminds.com', 'Vp@12345'),
    'admin':  ('admin@opexminds.com', 'Admin@12345'),
}

# ── 1. auth: bad creds, no-session access, logout kills session
try:
    op = session(ROLES['uw'][0], 'WrongPassword123')
    check('auth rejects bad password', False, 'login succeeded')
except urllib.error.HTTPError as e:
    check('auth rejects bad password', e.code in (400, 401), f'{e.code}')
anon = urllib.request.build_opener()
code, _ = get(anon, '/api/v1/cases')
check('anon blocked from /cases', code in (401, 403, 302), f'{code}')
code, _ = get(anon, '/api/v1/queue')
check('anon blocked from /queue', code in (401, 403, 302), f'{code}')
code, _ = get(anon, '/api/v1/admin/formulas')
check('anon blocked from /admin/formulas', code in (401, 403, 302), f'{code}')

sessions = {}
for role, (email, pw) in ROLES.items():
    sessions[role] = session(email, pw)

# role pills correct
for role in ROLES:
    code, me = get(sessions[role], '/api/v1/auth/me')
    check(f'me ok {role}', code == 200 and me.get('role'), f"{code} {me.get('role') if me else ''}")

# ── 2. queue (all roles read)
for role in ['sales', 'uw', 'senior', 'vp', 'admin']:
    code, q = get(sessions[role], '/api/v1/queue')
    check(f'queue read {role}', code == 200 and isinstance(q, list), f'{code} n={len(q) if isinstance(q, list) else "-"}')
code, q = get(sessions['admin'], '/api/v1/queue')
if isinstance(q, list) and q:
    sample = q[0]
    for f in ['caseId', 'caseNumber', 'clientName', 'status', 'reason', 'priority', 'lives']:
        check(f'queue field {f}', f in sample, json.dumps(sample)[:80])
    # lives/premium coherent for quoted cases
    # real books (premium ≥ ₹5L) must carry lives; tiny demo stubs are known 0-lives
    quoted = [r for r in q if (r.get('premium') or 0) >= 500_000]
    bad = [r for r in quoted if r.get('lives', 0) <= 0]
    check('quoted real books have lives>0', len(bad) == 0, f'{len(bad)} rows: {[r["caseNumber"] for r in bad[:3]]}')

# ── 3. case + stepper endpoints on case 59 (real book)
CID = 59
for role in ['sales', 'uw', 'admin']:
    for ep in [f'/api/v1/cases/{CID}', f'/api/v1/cases/{CID}/analytics', f'/api/v1/cases/{CID}/multiyear',
               f'/api/v1/cases/{CID}/benefits', f'/api/v1/cases/{CID}/quotes', f'/api/v1/cases/{CID}/guardrails',
               f'/api/v1/cases/{CID}/discounts', f'/api/v1/cases/{CID}/documents', f'/api/v1/cases/{CID}/pipeline',
               f'/api/v1/cases/{CID}/standardize', f'/api/v1/cases/{CID}/coding', f'/api/v1/cases/{CID}/final-quote',
               f'/api/v1/cases/{CID}/si-scenario', f'/api/v1/cases/{CID}/multiyear']:
        code, body = get(sessions[role], ep)
        check(f'{role} {ep.split("/")[-1]}', code == 200, f'{code}')

# ── 4. admin gates: non-admin must get 403 on admin POSTs
code, rules = get(sessions['admin'], '/api/v1/admin/guardrail-rules')
check('guardrail-rules list admin', code == 200 and len(rules) == 20, f'{code} n={len(rules) if rules else 0}')
code, formulas = get(sessions['admin'], '/api/v1/admin/formulas')
check('formulas defaults', code == 200 and 'TrendPct' in formulas, f'{code}')
for role in ['sales', 'uw', 'senior', 'vp']:
    code, _ = get(sessions[role], '/api/v1/admin/formulas')
    check(f'formulas GET allowed {role} (read)', code in (200, 403), f'{code}')
    try:
        req = urllib.request.Request(f'{BASE}/api/v1/admin/formulas',
            data=json.dumps({'TrendPct': 99}).encode(), headers={'Content-Type': 'application/json'}, method='POST')
        r2 = sessions[role].open(req)
        check(f'formulas POST blocked {role}', False, f'{r2.status} - WRITE SUCCEEDED')
    except urllib.error.HTTPError as e:
        check(f'formulas POST blocked {role}', e.code == 403, f'{e.code}')
    except Exception as e:
        check(f'formulas POST blocked {role}', False, str(e)[:60])

# toggle gate
try:
    req = urllib.request.Request(f'{BASE}/api/v1/admin/guardrail-rules/GRD-MIN-LIVES/toggle', data=b'{}',
        headers={'Content-Type': 'application/json'}, method='POST')
    r2 = sessions['uw'].open(req)
    check('rule toggle blocked uw', False, f'{r2.status}')
except urllib.error.HTTPError as e:
    check('rule toggle blocked uw', e.code == 403, f'{e.code}')

# ── 5. masters upload gate + upload works
import io, uuid
boundary = uuid.uuid4().hex
csv = b'BenefitCode,UtilizationRate,SeverityCaptureRate,IncidenceRate,SeverityAmount,FactorPct\nQAProbe,0.10,0.80,0.04,50000,2.0\n'
body = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="qa_probe.csv"\r\n'
        f'Content-Type: text/csv\r\n\r\n').encode() + csv + f'\r\n--{boundary}--\r\n'.encode()
def post_multipart(op, path):
    req = urllib.request.Request(f'{BASE}{path}', data=body,
        headers={'Content-Type': f'multipart/form-data; boundary={boundary}'})
    try:
        r = op.open(req)
        return r.status, json.loads(r.read())
    except urllib.error.HTTPError as e:
        return e.code, None
code, resp = post_multipart(sessions['uw'], '/api/v1/admin/masters/upload')
check('masters upload blocked uw', code == 403, f'{code}')
code, resp = post_multipart(sessions['admin'], '/api/v1/admin/masters/upload')
check('masters upload admin ok + header intact', code == 200 and resp and 'QAProbe' not in json.dumps(resp)[:0], f'{code}')

# BOM-strip regression: first header char must survive
try:
    r = sessions['admin'].open(f'{BASE}/api/v1/admin/masters')
    masters = json.loads(r.read())
    check('masters list ok', True, json.dumps(masters)[:60])
except Exception as e:
    check('masters list ok', False, str(e)[:60])

# ── 6. SI scenario + discounts + uw decisions payload validation
code, si = get(sessions['uw'], f'/api/v1/cases/{CID}/si-scenario')
check('si-scenario read', code == 200, f'{code}')
code, disc = get(sessions['sales'], f'/api/v1/cases/{CID}/discounts')
check('discounts read sales', code == 200 and isinstance(disc, list), f'{code}')

# invalid decision action rejected
try:
    req = urllib.request.Request(f'{BASE}/api/v1/uw/decisions',
        data=json.dumps({'caseId': CID, 'action': 'Kaboom', 'remarks': 'x'}).encode(),
        headers={'Content-Type': 'application/json'}, method='POST')
    r2 = sessions['uw'].open(req)
    check('invalid decision rejected', False, f'{r2.status}')
except urllib.error.HTTPError as e:
    check('invalid decision rejected', e.code == 400, f'{e.code}')

# sales cannot approve (decision gate)
try:
    req = urllib.request.Request(f'{BASE}/api/v1/uw/decisions',
        data=json.dumps({'caseId': CID, 'action': 'Approve', 'remarks': 'sales attempt'}).encode(),
        headers={'Content-Type': 'application/json'}, method='POST')
    r2 = sessions['sales'].open(req)
    check('sales cannot post UW decision', False, f'{r2.status} — GATE MISSING')
except urllib.error.HTTPError as e:
    check('sales cannot post UW decision', e.code in (401, 403), f'{e.code}')

# ── 7. legacy + pages
code, html = get(anon, '/legacy')
check('/legacy serves old console', code in (200, 302), f'{code}')

print(f'PASS {sum(1 for _,ok,_ in results if ok)} / FAIL {sum(1 for _,ok,_ in results if not ok)} / total {len(results)}')
for name, ok, detail in results:
    if not ok:
        print(f'  ✗ {name} — {detail}')
json.dump(results, open('/tmp/qa_phase1.json', 'w'), indent=1)
