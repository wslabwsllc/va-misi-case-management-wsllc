# Docker setup and troubleshooting

## How the containers are put together

One multi-stage `Dockerfile` builds the solution once (Release), publishes `web` and `migrator`, and produces the shared
`caseintake-eval:local` image used by `migrator`, `web`, the test copy (`migrator-e2e`, `web-e2e`) and `tests`. The image
contains the published apps (`/app/web`, `/app/migrator`), the compiled tests (`/src`), the NuGet cache, and Playwright
Chromium with its Linux packages (`/ms-playwright`). Nothing is restored, built or downloaded when containers run.

| Service | Purpose | Notes |
|---|---|---|
| `db` | PostgreSQL 17 | volume `pgdata` |
| `migrator` | migrate + seed (demo DB `caseintake`), reconcile attachments | `working_dir: /app/migrator` |
| `veteran-lookup` | synthetic lookup mock | Compose network only |
| `web` | the app on <http://localhost:8080> | `working_dir: /app/web`; volumes `attachments`, `dataprotection`, `testresults` |
| `migrator-e2e`, `web-e2e` | profile `tests`: an isolated copy of the production app for browser tests (DB `caseintake_e2e`) | not published to the host |
| `tests` | profile `tests`: `scripts/run-tests.sh` | writes runs to the `testresults` volume |

Both web containers are healthy only when `/health/ready` **and** `/_framework/blazor.web.js` answer 200.

## Root causes fixed in this release

### 1. `blazor.web.js` and every asset answered with the sign-in redirect

Symptom: in Docker the pages rendered (prerendered HTML) but nothing was interactive, styles were missing, and the browser
console showed the framework script refused because its MIME type was `text/html`. `curl -I http://localhost:8080/_framework/blazor.web.js`
returned `302 Location: /account/login?ReturnUrl=%2F_framework%2Fblazor.web.js`.

Cause: the image's `WORKDIR` is `/src` (the tests service needs it), and `web` started `dotnet /app/web/CaseIntake.Web.dll`
from there, so its **content root was `/src`** (log: `"Content root path: /src"`). `/src/wwwroot` does not exist, so
static files were never found; the request fell through to endpoint routing, where the authenticated-user fallback policy
challenged it and the cookie handler redirected to the sign-in page. The published files were present all along in
`/app/web/wwwroot`.

Fix (source level, no container patching):
- Compose sets `working_dir: /app/web` for `web`/`web-e2e` (and `/app/migrator` for the migrators); the image's default
  command changes into `/app/web` first, so a bare `docker run` also works.
- `Program.cs` fails at startup with a clear message when the web root lacks the published assets (`WebRootGuard`).
- Static assets are served by `MapStaticAssets().AllowAnonymous()` and referenced with `@Assets[...]` (fingerprinted,
  compressed, immutable caching); the one framework bootstrap script is `_framework/blazor.web.js` from the
  `Microsoft.AspNetCore.App.Internal.Assets` package (no copied or CDN script, no duplicate).
- The build stage asserts that `/app/web/wwwroot/_framework/blazor.web.js`, `app.css` and the static assets endpoints
  manifest exist in the publish output.
- The health check also probes `/_framework/blazor.web.js`.

### 2. `bash\r: No such file or directory` (unhealthy web container, tests service would not start)

Symptom: `docker compose ps` showed `web (unhealthy)`; `docker inspect` showed `/usr/bin/env: 'bash\r'`.

Cause: on Windows checkouts with `core.autocrlf=true`, `scripts/*.sh` had CRLF line endings, and `COPY . .` put them in
the image.

Fix: `.gitattributes` forces LF for `*.sh` (and `Dockerfile`, `*.yml`); the Dockerfile also strips CRs from the scripts,
so even an old working copy builds a working image. A fresh clone gets LF files. In an existing Windows clone, replace
the CRLF copies once (the repository content is already LF): delete `scripts/*.sh`, then run `git checkout -- scripts`.

## Checks

```bash
docker compose up --build -d          # demo stack
scripts/smoke-docker.sh               # framework JS delivery, content root, published assets (expects SMOKE: PASS)
docker compose logs web | grep -o '"ContentRoot":"[^"]*"'    # must be /app/web
curl -sI http://localhost:8080/_framework/blazor.web.js       # 200, content-type: text/javascript
```

In the browser (F12): the Network tab shows `blazor.web.<hash>.js` 200 and a WebSocket to `/_blazor` with status 101;
the Console has no errors. The automated equivalent is `CaseIntake.E2E.FrameworkDeliveryTests`.

## Other problems

| Symptom | Likely cause | What to do |
|---|---|---|
| `web` exits at start: "The web root ... does not contain the published static assets" | started outside `/app/web` | keep `working_dir: /app/web`, or set `ASPNETCORE_CONTENTROOT=/app/web` |
| Reconnect dialog appears and stays | the circuit's WebSocket is blocked (proxy) or the server restarted | allow WebSocket upgrades to `/_blazor`; Retry or Reload in the dialog |
| CSP errors in the console | an inline script was added | load scripts from files; the CSP is `script-src 'self'` (no `<ImportMap />`) |
| `tests` reports `FAILED TO START (application not reachable)` | `web-e2e` is not healthy | `docker compose --profile tests logs web-e2e`; `docker compose --profile tests up -d web-e2e` |
| Playwright: "Executable doesn't exist at /ms-playwright/..." | an image built before the E2E project | `docker compose --profile tests build --no-cache` |
| Test results page is empty | runs not imported yet | wait a minute (background import) or press **Import new runs**; check `docker compose logs web` for event 5202 |
| Port 8080 in use | another stack | `docker compose ls`; stop it; never use `down -v` unless you mean to delete data |

`docker compose down` keeps all volumes (data, attachments, keys, test history). `docker compose down -v` deletes them.
