# Veteran Lookup mock (E4-S1). Built by docker-compose.yml with the repository root as context. Only the projects the
# mock needs are copied, so changes elsewhere do not invalidate this image. The Government contract (specification 4.6)
# is copied from docs/contracts because the mock embeds and serves it verbatim. The runtime image includes bash, which the
# Compose healthcheck uses (no curl is installed). Packages come from the registry at build time only.
# Images are pinned by digest and the restore runs in locked mode against the committed packages.lock.json files
# (docs/versions.md).
# The SDK stage runs on the build platform and the runtime on the target platform (see the root Dockerfile).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.402-noble@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props BannedSymbols.txt .editorconfig ./
COPY src/CaseIntake.Domain/CaseIntake.Domain.csproj src/CaseIntake.Domain/packages.lock.json src/CaseIntake.Domain/
COPY src/CaseIntake.Contracts/CaseIntake.Contracts.csproj src/CaseIntake.Contracts/packages.lock.json src/CaseIntake.Contracts/
COPY src/CaseIntake.ServiceDefaults/CaseIntake.ServiceDefaults.csproj src/CaseIntake.ServiceDefaults/packages.lock.json src/CaseIntake.ServiceDefaults/
COPY src/CaseIntake.VeteranLookup.Mock/CaseIntake.VeteranLookup.Mock.csproj src/CaseIntake.VeteranLookup.Mock/packages.lock.json src/CaseIntake.VeteranLookup.Mock/
RUN dotnet restore src/CaseIntake.VeteranLookup.Mock/CaseIntake.VeteranLookup.Mock.csproj --locked-mode
COPY src/CaseIntake.Domain/ src/CaseIntake.Domain/
COPY src/CaseIntake.Contracts/ src/CaseIntake.Contracts/
COPY src/CaseIntake.ServiceDefaults/ src/CaseIntake.ServiceDefaults/
COPY src/CaseIntake.VeteranLookup.Mock/ src/CaseIntake.VeteranLookup.Mock/
COPY docs/contracts/veteran-lookup.spec.openapi.yaml docs/contracts/
RUN dotnet publish src/CaseIntake.VeteranLookup.Mock/CaseIntake.VeteranLookup.Mock.csproj -c Release --no-restore \
      -p:UseAppHost=false -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS runtime
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 ASPNETCORE_URLS=http://+:8081 ASPNETCORE_ENVIRONMENT=Production
RUN useradd --uid 10001 --create-home --shell /bin/bash caseintake
COPY --from=build --chown=caseintake:caseintake /app /app
USER caseintake
WORKDIR /app
EXPOSE 8081
ENTRYPOINT ["dotnet", "/app/CaseIntake.VeteranLookup.Mock.dll"]
