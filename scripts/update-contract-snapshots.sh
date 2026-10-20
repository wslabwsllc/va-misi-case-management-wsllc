#!/usr/bin/env bash
# Regenerates the committed file that is generated from code:
#   src/CaseIntake.VeteranLookup.Mock/Data/veterans.json   synthetic dataset from VeteranDatasetGenerator
# The lookup contract is NOT generated: docs/contracts/veteran-lookup.spec.openapi.yaml is the Government's definition
# (specification 4.6), committed verbatim and pinned by SHA-256 (ADR 0008). Nothing here rewrites it.
# Review the resulting changes before committing; the snapshot tests fail until code and files agree.
set -euo pipefail
cd "$(dirname "$0")/.."
project=tests/CaseIntake.VeteranLookup.Tests
UPDATE_SNAPSHOTS=1 dotnet test "$project" --filter "FullyQualifiedName~VeteranDatasetTests&Category=Snapshot"
# Rebuild (the mock embeds the dataset) and confirm the snapshot now matches.
dotnet build "$project"
dotnet test "$project" --no-build --filter "Category=Snapshot"
echo "Snapshot regenerated. Review the changes to src/CaseIntake.VeteranLookup.Mock/Data/."
