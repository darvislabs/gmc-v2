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
            return Results.Content(ShellV3(), "text/html; charset=utf-8");
        });

        app.MapGet("/login", () => Results.Content(LoginPage(), "text/html; charset=utf-8"));

        app.MapGet("/case/{id:long}", (HttpContext ctx) =>
        {
            if (ctx.Session.GetString("userId") is null)
                return Results.Redirect("/login");
            return Results.Content(ShellV3(), "text/html; charset=utf-8");
        });

        app.MapGet("/exceptions", (HttpContext ctx) =>
        {
            if (ctx.Session.GetString("userId") is null) return Results.Redirect("/login");
            return Results.Content(ShellV3(), "text/html; charset=utf-8");
        });

        app.MapGet("/users", (HttpContext ctx) =>
        {
            if (ctx.Session.GetString("userId") is null) return Results.Redirect("/login");
            return Results.Content(ShellV3(), "text/html; charset=utf-8");
        });

        app.MapGet("/admin", (HttpContext ctx) =>
        {
            if (ctx.Session.GetString("userId") is null) return Results.Redirect("/login");
            return Results.Content(ShellV3(), "text/html; charset=utf-8");
        });

        // /legacy: the previous sidebar-console shell, parked for regression reference.
        app.MapGet("/legacy", (HttpContext ctx) =>
        {
            if (ctx.Session.GetString("userId") is null) return Results.Redirect("/login");
            return Results.Content(Shell(), "text/html; charset=utf-8");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // v3 role-first shell (Dhrumil's approved prototype structure):
    // header (brand / case+role pills / UW Queue back / theme / logout) →
    // login rolecards → UW Queue → Sales Queue → 9-step stepper panels →
    // UW Referral Workbench → Admin formula console. All logic in app-v3.js.
    // ─────────────────────────────────────────────────────────────────────────
    private static string ShellV3() => """
<!doctype html>
<html lang="en" data-theme="dark">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<title>Group Health AI Underwriter · Powered by Opexminds</title>
<link rel="stylesheet" href="/css/opex.css">
<link rel="stylesheet" href="/css/opex-polish.css">
<link rel="stylesheet" href="/css/opex-v3.css">
</head>
<body>
<header class="v3-topbar">
  <div class="v3-brand-group">
    <div class="v3-brand-logo" aria-hidden="true">
      <svg width="20" viewBox="0 0 122.748 104.092" fill="currentColor"><path d="M36.2105 0.107336C39.1482 -0.0666115 40.9673 -0.221277 43.657 1.28483C52.9355 6.57392 62.7007 11.8034 71.6346 17.6386C76.9231 21.0929 74.9681 25.7565 70.8385 28.8908C67.1 31.728 63.6004 35.7381 59.5343 38.0828C59.5081 37.1348 59.4553 34.3346 58.9595 33.8232C56.6061 31.3947 40.7114 22.5166 38.3065 21.8118C36.0507 22.0044 21.5038 30.8391 19.0072 32.914C17.5333 34.1387 18.1493 42.2768 18.2456 44.8952C19.9978 46.4786 23.8916 48.5601 26.0265 49.8562C30.703 52.696 35.4436 55.3704 39.9939 58.4293C42.4862 57.2937 46.9247 53.3207 49.3099 51.5878C55.9746 46.89 61.6529 42.0656 68.2367 37.15C72.6931 33.8232 79.4026 27.2761 84.6744 25.5214C87.8053 24.4793 93.4975 28.3859 96.3521 30.0968L107.143 36.5393C111.261 38.9919 115.672 41.1394 119.542 43.9372C122.798 46.3158 122.746 51.9098 122.722 55.6176C122.645 63.4047 122.915 71.302 122.556 79.0724C122.405 82.3412 120.881 84.554 118.236 86.2835C113.233 89.3989 108.019 92.5658 102.832 95.367C97.9822 97.9854 91.6713 103.205 86.382 104.071C79.395 104.306 78.4793 102.529 72.7553 99.2799C67.7354 96.4545 62.755 93.5598 57.8152 90.5965C51.2274 86.6772 42.3893 83.1158 51.7374 75.2567C54.9699 72.5387 59.6677 67.4064 63.2489 65.6186C63.4741 67.077 63.75 70.0409 64.5266 70.8168C66.5405 72.8284 82.5311 82.2001 84.3594 82.3047C87.0906 82.4607 102.841 72.5776 104.861 70.9447C105.016 67.5617 105.083 62.5489 104.853 59.2029C102.786 57.1141 84.5232 46.7387 81.4028 45.0433C78.7989 46.6441 73.3274 51.1218 70.7207 53.1612L52.5426 67.4314C48.7813 70.4446 43.0788 75.9144 38.6771 77.7829C37.5666 78.0637 34.8869 78.1648 33.9037 77.6762C24.726 73.1163 16.0008 67.407 7.02644 62.4291C4.54199 60.9618 0.854415 58.7001 0.531851 55.4455C-0.416157 45.8855 0.210338 35.6194 0.123201 25.975C0.0909188 22.4152 2.07932 19.6938 4.91075 17.8335C9.88228 14.5671 15.2895 11.7225 20.4064 8.66436C24.9092 6.18825 31.3972 1.24816 36.2105 0.107336Z"/></svg>
    </div>
    <div>
      <div class="v3-brand">Group Health AI Underwriter</div>
      <div class="v3-sub">Powered by Opexminds.ai · Enterprise B2B Engine</div>
    </div>
  </div>
  <div class="v3-rolebar">
    <span class="ox-pill ox-pill--vivid" id="v3-case-pill">Not logged in</span>
    <span class="ox-pill" id="v3-role-pill">—</span>
    <button class="ox-btn ox-btn--ghost ox-btn--sm" id="v3-queue-btn" style="display:none">UW Queue</button>
    <button class="ox-btn ox-btn--ghost ox-btn--sm" id="v3-theme-btn">☀ THEME</button>
    <button class="ox-btn ox-btn--ghost ox-btn--sm" id="v3-logout-btn" style="display:none">Logout</button>
  </div>
</header>

<main id="v3-root"></main>

<script src="/js/views-analytics.js?v=21"></script>
<script src="/js/app-v3.js?v=1"></script>
</body>
</html>
""";

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
<script src="/js/views-analytics.js?v=19"></script>
<script src="/js/app.js?v=19"></script>
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
