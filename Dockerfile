# Shared evaluation image: published web + migrator, plus the SDK and compiled Release tests so that
# `docker compose run --rm tests` needs no restore, build or download at run time (plan E1-S2/E1-S7).
# The Playwright browsers/OS dependencies required by E5-S9 are added to this image when the E2E project lands.
FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false
WORKDIR /src
COPY . .
RUN dotnet restore CaseIntake.sln
RUN dotnet build CaseIntake.sln -c Release --no-restore
RUN dotnet publish src/CaseIntake.Web/CaseIntake.Web.csproj -c Release --no-build -o /app/web \
 && dotnet publish src/CaseIntake.MigrationService/CaseIntake.MigrationService.csproj -c Release --no-build -o /app/migrator

FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS eval
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_CLI_HOME=/home/caseintake \
    NUGET_PACKAGES=/home/caseintake/.nuget/packages \
    ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production
RUN useradd --uid 10001 --create-home --shell /bin/bash caseintake \
 && mkdir -p /results /attachments /dataprotection \
 && chown -R caseintake:caseintake /results /attachments /dataprotection
COPY --from=build --chown=caseintake:caseintake /src /src
COPY --from=build --chown=caseintake:caseintake /app /app
COPY --from=build --chown=caseintake:caseintake /root/.nuget/packages /home/caseintake/.nuget/packages
RUN chmod +x /src/scripts/*.sh
USER caseintake
WORKDIR /src
EXPOSE 8080
CMD ["dotnet", "/app/web/CaseIntake.Web.dll"]
