# 0010. Render mode: Interactive Server, account pages static SSR

* Status: accepted
* Owner: E5

## Context

Spec 4.5 asks for a Blazor Web App on a supported .NET LTS and a documented render-mode choice with its tradeoffs
(plan E5-S1, A2). The app calls in-process services (`ICaseService`, `IAttachmentService`, `IVeteranLookupClient`,
`ITestResultsService`) behind the E3 permission policies, streams uploads into the attachment store, and has no public
REST API.

## Decision

- `AddRazorComponents().AddInteractiveServerComponents()` and `MapRazorComponents<App>().AddInteractiveServerRenderMode()`.
- Interactivity is applied at the router: `App.razor` renders `Routes` and `HeadOutlet` with `InteractiveServer` for
  every request that accepts interactive routing. The Identity account pages (`/account/login`, `/account/access-denied`)
  carry `[ExcludeFromInteractiveRouting]` and stay static SSR, so sign-in and sign-out are plain HTTP form posts that
  can set and clear the auth cookie. `/error` is static too.
- Prerendering stays on: the first response is complete HTML (fast first paint, works before the circuit connects);
  pages wait for the circuit only to make controls interactive.
- The .NET 10 `ReconnectModal` pattern gives an accessible reconnect dialog; the Router's `NotFoundPage` and
  `NavigationManager.NotFound()` give a real 404 page.
- Static assets are served by `MapStaticAssets()` (fingerprinted, compressed, immutable caching) and referenced through
  `@Assets[...]`, including the single framework script `_framework/blazor.web.js`. No `<ImportMap />` (it is an inline
  script, which the CSP `script-src 'self'` forbids); JS modules are imported by URL.

## Alternatives considered

| | Interactive Server (chosen) | Interactive WebAssembly | Interactive Auto |
|---|---|---|---|
| Latency | Every UI event is a SignalR round trip (fine on a LAN/localhost; noticeable on slow links) | UI runs locally after download | Server first, WebAssembly after download |
| Startup | Small download, immediate interactivity | Downloads the .NET runtime and app (several MB) before interactive | Fast first visit, large download in the background |
| Scalability | Server keeps a circuit (memory) per open tab; scales by memory and sticky connections | Stateless server; scales like an API | Both costs |
| Offline | Needs the connection; disconnects show the reconnect dialog | Could work offline for local logic, but every operation here needs the server | Same as WebAssembly once loaded |
| Auth | Server-side `AuthenticationStateProvider` with revalidation; services read the circuit's principal | Needs an API with token or cookie auth, and duplicated authorization at the API | Needs both models and state persistence between modes |
| File streaming | `InputFile` streams over the circuit straight into `IAttachmentService`; downloads use the authorized HTTP endpoint | Upload via HTTP API; extra endpoint, CORS and size handling | Both paths |
| Code | Calls services directly; no API layer, no DTO duplication | Requires a public API and client-side copies of validation | Requires the API and careful component placement |

Static SSR for everything (enhanced forms) was rejected for the app pages: dialogs, live announcements, per-keystroke
counters and in-place editing need interactivity.

## Tradeoffs

Server mode ties each tab to one server process and needs a live connection; a dropped connection shows the
reconnect dialog and unsaved form input is kept only while the circuit survives. In exchange there is no API surface
to secure, authorization runs once on the server, and uploads stream without an extra endpoint.

## Consequences

- Scaling out needs sticky sessions (not in scope: one container).
- JS interop runs only from `OnAfterRenderAsync` or event handlers (never during prerendering) and tolerates
  `JSDisconnectedException` (`UiInterop`).
- The web container must start in its publish folder so its content root contains `wwwroot`; otherwise `MapStaticAssets`
  has no files and the browser never receives `blazor.web.js` (see [docker-troubleshooting.md](../docker-troubleshooting.md)).
