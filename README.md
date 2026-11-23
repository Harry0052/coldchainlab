# ColdChainLab

Local rehearsal for one synthetic refrigerator and one logger configuration. It is a training drill. It does not alarm a live unit, judge vaccine viability, or recommend what to do with any product.

`Synthetic demonstration only. Not a vaccine viability, disposition, or compliance judgment.`

P02 checks fixture files. The thermal simulation and the drill report are added in later phases. Validation does not claim those results yet. Expected results for the frozen drill are in `fixtures/oracle/power-loss-drill.md`.

## Prerequisites

- .NET SDK 10.0.404, or another SDK in the 10.0 feature band (`global.json` uses `rollForward: latestFeature`).
- No paid service, account, or network connection is required after the SDK is installed.

Check the SDK:

```bash
dotnet --version
```

This repository was verified with SDK 10.0.404.

## Install and test

From the repository root:

```bash
dotnet restore ColdChainLab.slnx
dotnet build ColdChainLab.slnx
dotnet test ColdChainLab.slnx
```

Restore uses the committed NuGet lock files. Runtime does not download fixture data.

## Validate a fixture

Accept the labeled power-loss scenario (exit 0):

```bash
dotnet run --project src/ColdChainLab.Cli -- validate fixtures/valid/power-loss-drill.scenario.json
```

Reject the negative thermal coefficient (exit 2). The message names `thermal.coefficientPerMinute`:

```bash
dotnet run --project src/ColdChainLab.Cli -- validate fixtures/invalid/negative-coefficient.scenario.json
```

Add `--json` for a machine-readable report on stdout. Diagnostics for usage errors go to stderr. `--help` lists the commands and exits 0.

Scenario time fields are minutes (`time.unit` is `min`). Temperatures are °C. Fault ids in a scenario are unique so a later alert can name the fault that produced it.

## Fixture provenance

`fixtures/manifest.json` records checksums, the closed-form method, and the method links. NIST IR 7656 and the CDC Vaccine Storage and Handling Toolkit are not redistributed. Generation uses no random seed.

## License

MIT. See `LICENSE`.
