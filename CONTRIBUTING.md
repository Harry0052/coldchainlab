# Contributing

ColdChainLab is a local rehearsal for one synthetic refrigerator logger. Changes should keep that boundary.

- Do not add live alarms, inventory, product-viability judgments, or refrigerator control.
- Do not commit credentials, real clinic data, or copied figures from NIST or CDC publications. Link those sources instead.
- Keep scenario time in minutes and temperature in °C.
- Add or update a fixture oracle when a detection result changes.

## Setup

Install the .NET 10 SDK (10.0.404 or a later 10.0 patch). From a clean checkout:

```bash
dotnet restore ColdChainLab.slnx --locked-mode
dotnet test ColdChainLab.slnx
dotnet run --project src/ColdChainLab.Cli -- demo --out artifacts/demo
```

The bundled drill should report a detection delay of 3 minutes. See `README.md` and `docs/validation.md`.

If you add a NuGet package, commit the updated `packages.lock.json`. CI restores with `--locked-mode`.

## Reports

Security reports go through the route in `SECURITY.md`, not through a public issue.
