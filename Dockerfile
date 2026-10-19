# Shared evaluation image: published web + migrator, plus the SDK and compiled Release tests so that
# `docker compose run --rm tests` needs no restore, build or download at run time (plan E1-S2/E1-S7).
# Playwright Chromium and its Linux dependencies (E5-S9) are installed at build time from the compiled E2E project.
FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS build
# The NuGet cache lives at the same path as in the eval image: the build's static web assets manifests record absolute
# package paths, which the in-process test host (WebApplicationFactory) resolves at run time.
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false \
    NUGET_PACKAGES=/home/caseintake/.nuget/packages
WORKDIR /src
COPY . .
RUN dotnet restore CaseIntake.sln
RUN dotnet build CaseIntake.sln -c Release --no-restore
# Publishing includes the static web assets: wwwroot, the framework's _framework/blazor.web.js (from the
# Microsoft.AspNetCore.App.Internal.Assets package), collocated *.razor.js modules and the endpoints manifest that
# MapStaticAssets serves them from.
RUN dotnet publish src/CaseIntake.Web/CaseIntake.Web.csproj -c Release --no-build -o /app/web \
 && dotnet publish src/CaseIntake.MigrationService/CaseIntake.MigrationService.csproj -c Release --no-build -o /app/migrator \
 && test -f /app/web/wwwroot/_framework/blazor.web.js && test -f /app/web/wwwroot/app.css \
 && test -f /app/web/CaseIntake.Web.staticwebassets.endpoints.json

FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS eval
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_CLI_HOME=/home/caseintake \
    NUGET_PACKAGES=/home/caseintake/.nuget/packages \
    PLAYWRIGHT_BROWSERS_PATH=/ms-playwright \
    ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production
RUN useradd --uid 10001 --create-home --shell /bin/bash caseintake \
 && mkdir -p /results /attachments /attachments-e2e /dataprotection /test-results /e2e-fixtures /ms-playwright \
 && chown -R caseintake:caseintake /results /attachments /attachments-e2e /dataprotection /test-results /e2e-fixtures /ms-playwright
COPY --from=build --chown=caseintake:caseintake /src /src
COPY --from=build --chown=caseintake:caseintake /app /app
COPY --from=build --chown=caseintake:caseintake /home/caseintake/.nuget/packages /home/caseintake/.nuget/packages
# Chromium for the browser tests, with its OS packages (needs root and network at build time only).
RUN /src/tests/CaseIntake.E2E/bin/Release/net10.0/.playwright/node/linux-x64/node \
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
EXPOSE 8080
# The web app must start from its publish folder so its content root (and wwwroot) is /app/web.
CMD ["bash", "-c", "cd /app/web && exec dotnet CaseIntake.Web.dll"]
