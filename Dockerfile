# Two images from one build:
#   runtime  caseintake-app:local   published web + migrator on the ASP.NET runtime image (migrator, web, *-e2e)
#   eval     caseintake-eval:local  SDK + compiled Release tests + Playwright Chromium (tests profile only), so that
#                                   `docker compose run --rm tests` needs no restore, build or download at run time.
# A plain `docker compose up --build` builds only `runtime`; the test build and the browser install are skipped.
#
# The SDK stages run on the BUILD platform (--platform=$BUILDPLATFORM), the final images on the target platform
# (linux/amd64, pinned in docker-compose.yml). Framework-dependent .NET output is architecture-neutral IL, so an Apple
# Silicon host compiles natively instead of running the SDK under x86 emulation; on x64 hosts both are the same.
# UseAppHost=false: every container starts `dotnet <app>.dll`, so no native launcher for the build architecture is
# emitted. PlaywrightPlatform: copy the node driver for the target architecture, not the build machine's.

# ---- Project files only, so the restore layer below is reused until a project or package pin changes. ----
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS projects
WORKDIR /src
COPY . .
RUN find . -type f ! -name '*.csproj' ! -name 'CaseIntake.sln' ! -name 'Directory.*.props' ! -name 'global.json' -delete \
 && find . -type d -empty -delete

# The NuGet cache lives at the same path as in the eval image: the build's static web assets manifests record absolute
# package paths, which the in-process test host (WebApplicationFactory) resolves at run time.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS restore
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false \
    NUGET_PACKAGES=/home/caseintake/.nuget/packages
WORKDIR /src
COPY --from=projects /src ./
RUN dotnet restore CaseIntake.sln
COPY . .

# ---- Demo app: publish only web and migrator. ----
# Publishing includes the static web assets: wwwroot, the framework's _framework/blazor.web.js (from the
# Microsoft.AspNetCore.App.Internal.Assets package), collocated *.razor.js modules and the endpoints manifest that
# MapStaticAssets serves them from.
FROM restore AS publish
RUN dotnet publish src/CaseIntake.Web/CaseIntake.Web.csproj -c Release --no-restore -p:UseAppHost=false -o /app/web \
 && dotnet publish src/CaseIntake.MigrationService/CaseIntake.MigrationService.csproj -c Release --no-restore -p:UseAppHost=false -o /app/migrator \
 && test -f /app/web/wwwroot/_framework/blazor.web.js && test -f /app/web/wwwroot/app.css \
 && test -f /app/web/CaseIntake.Web.staticwebassets.endpoints.json

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble AS runtime
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 \
    ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production
# libgssapi-krb5-2: Npgsql probes for GSSAPI on connect and logs an error without it (the SDK image ships it).
# The mount points exist and belong to the app user, so new named volumes start out writable by it.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/* \
 && useradd --uid 10001 --create-home --shell /bin/bash caseintake \
 && mkdir -p /attachments /attachments-e2e /dataprotection /test-results /e2e-fixtures \
 && chown -R caseintake:caseintake /attachments /attachments-e2e /dataprotection /test-results /e2e-fixtures
COPY --from=publish --chown=caseintake:caseintake /app /app
# Compose health probe (bash only; no curl in the image). CRs stripped in case of a CRLF working copy.
COPY --chmod=755 scripts/healthcheck.sh /app/healthcheck.sh
RUN sed -i 's/\r$//' /app/healthcheck.sh
LABEL org.opencontainers.image.title="caseintake-app"
USER caseintake
EXPOSE 8080
# The web app must start from its publish folder so its content root (and wwwroot) is /app/web.
WORKDIR /app/web
CMD ["dotnet", "CaseIntake.Web.dll"]

# ---- Test image: the whole solution compiled in Release. ----
FROM restore AS build
ARG TARGETARCH
RUN dotnet build CaseIntake.sln -c Release --no-restore -p:UseAppHost=false \
      -p:PlaywrightPlatform=linux-$([ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64)

FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS eval
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_CLI_HOME=/home/caseintake \
    NUGET_PACKAGES=/home/caseintake/.nuget/packages \
    PLAYWRIGHT_BROWSERS_PATH=/ms-playwright \
    ASPNETCORE_ENVIRONMENT=Production
RUN useradd --uid 10001 --create-home --shell /bin/bash caseintake \
 && mkdir -p /results /e2e-fixtures /ms-playwright \
 && chown -R caseintake:caseintake /results /e2e-fixtures /ms-playwright
COPY --from=build --chown=caseintake:caseintake /src /src
COPY --from=build --chown=caseintake:caseintake /home/caseintake/.nuget/packages /home/caseintake/.nuget/packages
# Chromium for the browser tests, with its OS packages (needs root and network at build time only).
RUN /src/tests/CaseIntake.E2E/bin/Release/net10.0/.playwright/node/linux-*/node \
      /src/tests/CaseIntake.E2E/bin/Release/net10.0/.playwright/package/cli.js install --with-deps chromium \
 && rm -rf /var/lib/apt/lists/* \
 && chown -R caseintake:caseintake /ms-playwright
# .gitattributes keeps scripts LF; normalizing here as well protects builds from a working tree checked out with CRLF.
RUN sed -i 's/\r$//' /src/scripts/*.sh && chmod +x /src/scripts/*.sh
# Build metadata recorded with every test run (scripts/run-all-tests.sh passes them; defaults otherwise).
ARG GIT_COMMIT=unknown
ARG GIT_BRANCH=unknown
ARG BUILD_ID=local
ENV CASEINTAKE_GIT_COMMIT=${GIT_COMMIT} CASEINTAKE_GIT_BRANCH=${GIT_BRANCH} CASEINTAKE_BUILD_ID=${BUILD_ID}
LABEL org.opencontainers.image.title="caseintake-eval" org.opencontainers.image.revision="${GIT_COMMIT}" \
      org.opencontainers.image.version="${BUILD_ID}"
USER caseintake
WORKDIR /src
CMD ["/src/scripts/run-tests.sh"]
