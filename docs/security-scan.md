# Dependency vulnerability scan

## NuGet packages

Date: 2026-10-20. Revision: `9672596` (main). Host: macOS arm64, .NET SDK 10.0.402. Source: nuget.org (GitHub
Advisory Database).

```bash
dotnet list CaseIntake.sln package --vulnerable --include-transitive
```

Result: **no vulnerable packages** in any of the 20 projects (12 `src`, 8 `tests`), direct or transitive.

## Container images

Not yet scanned. The images are pinned by digest in [versions.md](versions.md); scan them with Trivy or Docker Scout
after `docker compose build` and record the result here.
