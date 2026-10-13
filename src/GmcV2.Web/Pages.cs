using GmcV2.Core;

namespace GmcV2.Web;

/// <summary>Console routes: serves the Opexminds SPA shell. All logic lives in wwwroot/js/app.js.</summary>
public static class Pages
{
    public static void MapAll(WebApplication app)
    {
        app.MapGet("/", (HttpContext ctx) =>
        {
            if (ctx.Session.GetString("userId") is null)
                return Results.Redirect("/login");
            return Results.Content(Shell(), "text/html; charset=utf-8");
        });

        app.MapGet("/login", () => Results.Content(LoginPage(), "text/html; charset=utf-8"));

        app.MapGet("/case/{id:long}", (HttpContext ctx) =>
        {
            if (ctx.Session.GetString("userId") is null)
                return Results.Redirect("/login");
            return Results.Content(Shell(), "text/html; charset=utf-8");
        });

        app.MapGet("/exceptions", (HttpContext ctx) =>
        {
            if (ctx.Session.GetString("userId") is null) return Results.Redirect("/login");
            return Results.Content(Shell(), "text/html; charset=utf-8");
        });

        app.MapGet("/users", (HttpContext ctx) =>
        {
            if (ctx.Session.GetString("userId") is null) return Results.Redirect("/login");
            return Results.Content(Shell(), "text/html; charset=utf-8");
        });

        app.MapGet("/admin", (HttpContext ctx) =>
        {
            if (ctx.Session.GetString("userId") is null) return Results.Redirect("/login");
            return Results.Content(Shell(), "text/html; charset=utf-8");
        });
    }

    private static string Shell() => """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<title>GMC v2 — Opexminds Underwriting Console</title>
<link rel="stylesheet" href="/css/opex.css">
<link rel="stylesheet" href="/css/opex-polish.css">
</head>
<body>
<div class="ox-shell">
  <aside class="ox-sidebar" id="sidebar">
    <a class="ox-brand" href="#/">GMC <span class="ox-brand-sub">v2.0 · Opexminds</span></a>
    <nav class="ox-nav" id="nav"></nav>
    <div class="ox-sidebar-foot ox-sidebar-footer">
      <span class="ox-user-name" id="who-name">—</span>
      <span class="ox-user-role" id="who-role">—</span>
    </div>
  </aside>
  <div class="ox-main">
    <header class="ox-topbar">
      <button class="ox-burger" id="burger" aria-label="Menu">☰</button>
      <h1 class="ox-pagetitle" id="pagetitle">Cases</h1>
      <div class="ox-grow"></div>
      <div class="ox-bell-wrap">
        <button class="ox-bell" id="bell" aria-label="Notifications"><span class="ox-bell-badge ox-hide" id="bell-badge">0</span></button>
        <div class="ox-bell-menu ox-hide" id="bell-menu"></div>
      </div>
      <button class="ox-btn ox-btn--ghost ox-btn--sm" id="logout">Sign out</button>
    </header>
    <div class="ox-content" id="view"></div>
    <div class="ox-footer">GMC v2.0 · deterministic underwriting core · Opexminds design system</div>
  </div>
</div>
<script src="/js/app.js"></script>
</body>
</html>
""";

    private static string LoginPage() => """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<title>Sign in — GMC v2 · Opexminds</title>
<link rel="stylesheet" href="/css/opex.css">
<link rel="stylesheet" href="/css/opex-polish.css">
</head>
<body>
<div class="ox-login-wrap">
  <aside class="ox-login-aside">
    <div>
      <div class="aside-kicker">GMC v2.0 · Opexminds</div>
      <h1 class="aside-headline" style="margin-top:18px">Underwrite group health in minutes, not weeks.</h1>
      <p class="aside-sub">Thirteen automated stages — profile, map, code, analyze, guardrail, price — turn raw claims and enrollment files into a defensible quote.</p>
    </div>
    <div class="aside-rows">
      <div class="aside-row"><b>1,280 members · 4,180 claims</b><span>PRICED IN 0.87s</span></div>
      <div class="aside-row"><b>Auto-quote confidence</b><span>GUARDRAIL 97.7</span></div>
      <div class="aside-row"><b>Every decision audited</b><span>ops.AUDITLOG</span></div>
    </div>
  </aside>
  <main class="ox-login-main">
  <form class="ox-login-card" id="login-form">
    <div class="ox-brand ox-login-brand">GMC <span class="ox-brand-sub">v2.0 · Opexminds</span></div>
    <p class="ox-muted">AI-first group mediclaim underwriting. Sign in to continue.</p>
    <label class="ox-mono-label" for="email">EMAIL</label>
    <input class="ox-input" id="email" name="email" type="email" autocomplete="username" required placeholder="you@opexminds.com">
    <label class="ox-mono-label" for="password">PASSWORD</label>
    <input class="ox-input" id="password" name="password" type="password" autocomplete="current-password" required placeholder="••••••••">
    <button class="ox-btn ox-btn--primary ox-btn--lg ox-login-btn" type="submit">Sign in</button>
    <div class="ox-banner ox-banner--error ox-hide" id="login-error"></div>
    <div class="ox-hr"></div>
    <div class="ox-mono-label">DEMO ROLES — TAP TO FILL</div>
    <div class="ox-chip-row" id="chips"></div>
  </form>
  </main>
</div>
<script>
const USERS = [
  ["sales@opexminds.com","Sales@12345","Sales"],
  ["uw@opexminds.com","Uw@12345","Underwriter"],
  ["senior@opexminds.com","Senior@12345","Senior UW"],
  ["vp@opexminds.com","Vp@12345","VP"],
  ["admin@opexminds.com","Admin@12345","Admin"],
];
const row = document.getElementById('chips');
for (const [email, pw, label] of USERS) {
  const b = document.createElement('button');
  b.type = 'button'; b.className = 'ox-btn ox-btn--ghost ox-btn--sm';
  b.textContent = label;
  b.onclick = () => { document.getElementById('email').value = email; document.getElementById('password').value = pw; };
  row.appendChild(b);
}
document.getElementById('login-form').addEventListener('submit', async (e) => {
  e.preventDefault();
  const err = document.getElementById('login-error');
  err.classList.add('ox-hide');
  const r = await fetch('/api/v1/auth/login', { method: 'POST', headers: {'Content-Type':'application/json'},
    body: JSON.stringify({ email: document.getElementById('email').value, password: document.getElementById('password').value }) });
  if (r.ok) { location.href = '/'; }
  else { err.textContent = 'Invalid email or password.'; err.classList.remove('ox-hide'); }
});
</script>
</body>
</html>
""";
}
