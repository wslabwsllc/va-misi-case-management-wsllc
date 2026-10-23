# Veteran Service History mock (Change 2, D1). Built by docker-compose.yml with the repository root as context. Only the
# projects the mock needs are copied, so changes elsewhere do not invalidate this image. The runtime image includes bash,
# which the Compose healthcheck uses (no curl is installed). Packages come from the registry at build time only.
# Images are pinned by digest (the same digests as the Lookup mock) and the restore runs in locked mode against the
# committed packages.lock.json files (docs/versions.md).
# The SDK stage runs on the build platform and the runtime on the target platform (see the root Dockerfile).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.402-noble@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props BannedSymbols.txt .editorconfig ./
COPY src/CaseIntake.Domain/CaseIntake.Domain.csproj src/CaseIntake.Domain/packages.lock.json src/CaseIntake.Domain/
COPY src/CaseIntake.Contracts/CaseIntake.Contracts.csproj src/CaseIntake.Contracts/packages.lock.json src/CaseIntake.Contracts/
COPY src/CaseIntake.ServiceDefaults/CaseIntake.ServiceDefaults.csproj src/CaseIntake.ServiceDefaults/packages.lock.json src/CaseIntake.ServiceDefaults/
COPY src/CaseIntake.VeteranHistory.Mock/CaseIntake.VeteranHistory.Mock.csproj src/CaseIntake.VeteranHistory.Mock/packages.lock.json src/CaseIntake.VeteranHistory.Mock/
RUN dotnet restore src/CaseIntake.VeteranHistory.Mock/CaseIntake.VeteranHistory.Mock.csproj --locked-mode
COPY src/CaseIntake.Domain/ src/CaseIntake.Domain/
COPY src/CaseIntake.Contracts/ src/CaseIntake.Contracts/
COPY src/CaseIntake.ServiceDefaults/ src/CaseIntake.ServiceDefaults/
COPY src/CaseIntake.VeteranHistory.Mock/ src/CaseIntake.VeteranHistory.Mock/
RUN dotnet publish src/CaseIntake.VeteranHistory.Mock/CaseIntake.VeteranHistory.Mock.csproj -c Release --no-restore \
      -p:UseAppHost=false -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS runtime
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 ASPNETCORE_URLS=http://+:8082 ASPNETCORE_ENVIRONMENT=Production
RUN useradd --uid 10001 --create-home --shell /bin/bash caseintake
COPY --from=build --chown=caseintake:caseintake /app /app
USER caseintake
WORKDIR /app
EXPOSE 8082
ENTRYPOINT ["dotnet", "/app/CaseIntake.VeteranHistory.Mock.dll"]
