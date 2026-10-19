# Veteran Lookup mock (E4-S1). Built by docker-compose.yml with the repository root as context. Only the projects the
# mock needs are copied, so changes elsewhere do not invalidate this image. The runtime image includes bash, which the
# Compose healthcheck uses (no curl is installed). Packages come from the registry at build time only.
# The SDK stage runs on the build platform and the runtime on the target platform (see the root Dockerfile).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props BannedSymbols.txt .editorconfig ./
COPY src/CaseIntake.Domain/CaseIntake.Domain.csproj src/CaseIntake.Domain/
COPY src/CaseIntake.Contracts/CaseIntake.Contracts.csproj src/CaseIntake.Contracts/
COPY src/CaseIntake.ServiceDefaults/CaseIntake.ServiceDefaults.csproj src/CaseIntake.ServiceDefaults/
COPY src/CaseIntake.VeteranLookup.Mock/CaseIntake.VeteranLookup.Mock.csproj src/CaseIntake.VeteranLookup.Mock/
RUN dotnet restore src/CaseIntake.VeteranLookup.Mock/CaseIntake.VeteranLookup.Mock.csproj
COPY src/CaseIntake.Domain/ src/CaseIntake.Domain/
COPY src/CaseIntake.Contracts/ src/CaseIntake.Contracts/
COPY src/CaseIntake.ServiceDefaults/ src/CaseIntake.ServiceDefaults/
COPY src/CaseIntake.VeteranLookup.Mock/ src/CaseIntake.VeteranLookup.Mock/
RUN dotnet publish src/CaseIntake.VeteranLookup.Mock/CaseIntake.VeteranLookup.Mock.csproj -c Release --no-restore \
      -p:UseAppHost=false -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble AS runtime
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 ASPNETCORE_URLS=http://+:8081 ASPNETCORE_ENVIRONMENT=Production
RUN useradd --uid 10001 --create-home --shell /bin/bash caseintake
COPY --from=build --chown=caseintake:caseintake /app /app
USER caseintake
WORKDIR /app
EXPOSE 8081
ENTRYPOINT ["dotnet", "/app/CaseIntake.VeteranLookup.Mock.dll"]
