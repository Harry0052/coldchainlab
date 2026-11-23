# ColdChainLab

Version 0.1.0. Local rehearsal for one synthetic refrigerator and one logger configuration. A coordinator or trainer can see whether that configuration would notice a simulated power loss, including how late the alert is and whether missing samples hide the excursion.

This is a training drill. It does not alarm a live unit, track vaccine inventory, judge product viability, or tell anyone what to do with a product.

`Synthetic demonstration only. Not a vaccine viability, disposition, or compliance judgment.`

## Supported platform

The tool is a .NET 10 console app. It runs on the operating systems that the .NET 10 SDK supports. The commands below were run on Linux x86_64 with SDK 10.0.404. There is no hardware, instrument, or paid-service demo.

## Prerequisites

- .NET SDK 10.0.404, or another SDK in the 10.0 feature band (`global.json` uses `rollForward: latestFeature`).
- No paid service, account, or network connection is required after the SDK is installed.

```bash
dotnet --version
```

Verified with SDK 10.0.404.

## Clean install

```bash
git clone https://github.com/Harry0052/coldchainlab.git
cd coldchainlab
dotnet --version
dotnet restore ColdChainLab.slnx --locked-mode
dotnet test ColdChainLab.slnx
```

Restore uses the committed NuGet lock files. The drill does not download data.

## Demo

After the install above, one command runs the bundled drill:

```bash
dotnet run --project src/ColdChainLab.Cli -- demo --out artifacts/demo
```

Exit 0. The terminal line `detection-delay-min 3 fault power-loss-1` is the expected result. The human-readable report is `artifacts/demo/power-loss-drill.summary.txt`. It should contain `detection delay: 3 min, fault power-loss-1, detected at minute 15`. The machine-readable copy is `artifacts/demo/power-loss-drill.events.json`.

The export directory also contains:

- `power-loss-drill.scenario.json` — the scenario that was run
- `power-loss-drill.temperature.csv` — simulated ground truth, one row per minute
- `power-loss-drill.observations.csv` — samples the logger kept
- `power-loss-drill.events.json` — machine-readable findings
- `power-loss-drill.summary.txt` — human-readable table and findings

Inspect the summary. The detection delay for this drill is **3 minutes**: the continuous trace reaches 8.0 °C at minute 12, and the 5-minute logger first records that at minute 15. The finding names fault `power-loss-1`, which is the same id as in the exported scenario file.

Time fields are minutes. Temperatures are °C. The summary repeats the simulation label, the demonstration thresholds, and the limitation next to the findings.

The same drill with the minute-15 and minute-20 samples removed:

```bash
dotnet run --project src/ColdChainLab.Cli -- demo dropout --out artifacts/demo-dropout
```

That report has no excursion alert. It has a missed excursion for `power-loss-1` and a separate gap alert for `logger-dropout-1`. Gap minutes are marked `gap` in the timeline and have no observed temperature.

`--json` prints the event report on stdout and leaves diagnostic text off that stream. `--help` lists commands and exits 0.

## Run a scenario file

```bash
dotnet run --project src/ColdChainLab.Cli -- run fixtures/valid/stable-hold.scenario.json --out artifacts/stable-hold
```

`stable-hold` stays at 5.0 °C and raises no excursion alert. `validate` checks a file without simulating:

```bash
dotnet run --project src/ColdChainLab.Cli -- validate fixtures/valid/power-loss-drill.scenario.json
```

## When the input is rejected

A negative thermal coefficient is rejected before any report is written. Exit code 2. The message names the field and the allowed range:

```bash
dotnet run --project src/ColdChainLab.Cli -- run fixtures/invalid/negative-coefficient.scenario.json --out artifacts/rejected
```

Other usage errors also exit 2. Exit 1 means the scenario was valid and the output directory could not be written. Exit 0 means the command finished.

## What the numbers mean

The hand calculation, the closed-form coefficient, and the expected event files are in `fixtures/oracle/power-loss-drill.md`. Checksums and the method links are in `fixtures/manifest.json`. NIST IR 7656 and the CDC Vaccine Storage and Handling Toolkit are linked there as method context. Their figures and measurements are not in this repository. Thresholds in the drill are demonstration values.

## Fixture terms

Temperatures and wording in `fixtures/` are original and are covered by the MIT license. They were generated from the closed form in `fixtures/oracle/power-loss-drill.md` with no random seed. NIST IR 7656 and the CDC Vaccine Storage and Handling Toolkit are method links in `fixtures/manifest.json`. Those publications are not copied into this repository. Do not treat the 2 °C and 8 °C demonstration thresholds as a storage standard.

## Project files

- `CHANGELOG.md` — version history
- `CONTRIBUTING.md` — how to build a change
- `SECURITY.md` — where to report a vulnerability
- `docs/validation.md` — acceptance evidence
- `LICENSE` — MIT

## License

MIT. See `LICENSE`.
