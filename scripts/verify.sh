#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
npm ci --ignore-scripts
npm run assets
dotnet restore DigitalSorter.slnx --locked-mode
dotnet build DigitalSorter.slnx --no-restore --configuration Release -warnaserror
dotnet test tests/DigitalSorter.Core.Tests --no-restore --configuration Release \
  -p:CollectCoverage=true -p:CoverletOutput="$PWD/test-results/core/" -p:CoverletOutputFormat=cobertura \
  -p:Threshold=80 '-p:ThresholdType=line%2cbranch' -p:ThresholdStat=total
dotnet test tests/DigitalSorter.IntegrationTests --no-build --configuration Release --logger "trx;LogFileName=integration.trx" --results-directory test-results
scripts/test-browser.sh
