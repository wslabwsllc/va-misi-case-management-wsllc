#!/usr/bin/env bash
# Regenerates the committed files that are generated from code (ADR 0008):
#   docs/contracts/veteran-lookup.openapi.yaml       OpenAPI document generated from the Veteran Lookup mock
#   src/CaseIntake.VeteranLookup.Mock/Data/veterans.json   synthetic dataset from VeteranDatasetGenerator
# Review the resulting changes before committing; the snapshot tests fail until code and files agree.
# The dataset is regenerated first because the mock embeds it.
set -euo pipefail
cd "$(dirname "$0")/.."
project=tests/CaseIntake.VeteranLookup.Tests
UPDATE_SNAPSHOTS=1 dotnet test "$project" --filter "FullyQualifiedName~VeteranDatasetTests&Category=Snapshot"
dotnet build "$project"
UPDATE_SNAPSHOTS=1 dotnet test "$project" --no-build --filter "Category=Snapshot"
echo "Snapshots regenerated. Review the changes to docs/contracts/ and src/CaseIntake.VeteranLookup.Mock/Data/."
