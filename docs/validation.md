# Validation

Checked on 2026-11-23. Every result below is from the synthetic fixtures in this repository. Nothing here was compared with a laboratory refrigerator or a clinic logger file.

## Environment

- OS: Linux 6.12.113+ x86_64
- SDK: .NET 10.0.404 (`dotnet --version`), runtime 10.0.15
- `dotnet format ColdChainLab.slnx --verify-no-changes`: exit 0
- `dotnet test ColdChainLab.slnx`: Passed, 26 tests, 0 failed
- `dotnet list ColdChainLab.slnx package --vulnerable --include-transitive`: no vulnerable packages for the CLI, core, or test projects

The test host prints a notice when `VSTEST_ARTIFACTS_DIRECTORY` is unset. Setting that variable removes the notice. It does not change the assertions.

## Tolerances

The power-loss curve is the closed form

`T(t) = 25 + (5 - 25) * exp(-0.013543244125 * t)`

with `t` in minutes. Tests compare engine temperatures with that expression within `1e-6` °C, and with the six-decimal CSV within `5e-7` °C. Event times in the frozen drill are whole minutes and match exactly (`delayMinutes` 3, detection at minute 15, gap alert at minute 20). This is a synthetic fixture, not a calibrated measurement.

## Acceptance cases

| Case | Expected | Observed |
| --- | --- | --- |
| Stable hold, cooling on, no fault, initial = target = 5 °C | `T(t) = 5` for the whole run. No excursion alert. | `SimulationTests.Stable_hold_stays_at_the_target_and_raises_no_excursion`. Every truth and observed row is 5 °C. Event list empty. |
| Power loss, door closed | Temperature rises at every whole minute and stays below 25 °C. `T(12) = 8` °C. | Same test class, `Power_loss_matches_the_closed_form_and_warms_toward_ambient`. Monotonic, each row below ambient, minute 12 within `1e-6` of 8 °C. |
| Same truth, cadence 5 min vs 10 min | 5-minute logger detects at minute 15 (delay 3). 10-minute logger detects at minute 20 (delay 8). | `Slower_sampling_detects_the_same_excursion_later`. Delays 3 and 8. |
| Dropout of minutes 15 and 20 | Truth at minute 15 is unchanged. No excursion alert. Missed excursion uses fault `power-loss-1`. Gap alert uses fault `logger-dropout-1` at minute 20. | `Dropout_reports_a_missed_excursion_and_a_separate_gap`. |
| Logger recovery | Samples at 0, 5, 10, and 25. Two evidence segments. Late alert at minute 25 (delay 13) and a closed gap that keeps its own fault id. | `Logger_recovery_opens_a_second_evidence_segment`. |
| Clean 5 °C trace | No excursion alert. | Stable-hold result above. |
| Invalid coefficient | Reject `thermal.coefficientPerMinute` of `-0.2` before writing a report. | `validate` and `run` exit 2. Message: must be greater than 0 and at most 5 per minute. `docs/examples/negative-coefficient.txt`. |
| Invalid timing and ids | Duration 1441 minutes rejected. Scenario id `../escape` rejected. Cadence 0 rejected. Files over 65536 bytes rejected. | `Unreasonably_long_runs_and_unsafe_ids_are_rejected`, `Non_positive_cadence_is_invalid_timing`, `Oversized_payload_is_rejected_before_it_is_treated_as_a_scenario`. |
| Units through export | Reports use `min` and `C`. CSV header is `minute_min,timestamp_utc,temperature_c`. | Event JSON and summary files under `docs/examples/power-loss-drill/`. |
| Frozen metric | Delay 3 minutes on the observed drill, and the dropout replay separates the miss from the gap. | Fresh `demo` on 2026-11-23, warm `dotnet run --no-build`, exit 0 in 0.521 s. Stdout: `detection-delay-min 3 fault power-loss-1`. Dropout summary in `docs/examples/power-loss-dropout/power-loss-dropout.summary.txt`. |

Detection uses logger samples in time order. It does not read a later sample to decide an earlier alert. A gap is raised when silence reaches `dropoutTimeoutMinutes`, not by changing the simulated temperature.

## Trust boundaries

- Input is a local scenario file. The parser requires schema version 1, rejects unknown fields, and stops at 65536 bytes.
- Scenario and fault ids are a short lowercase token, so output file names cannot leave the chosen directory.
- `ResultWriter` writes only paths whose full path is inside the `--out` directory.
- The program does not start a process, open a socket, or read a secret. There is no service account.
- Demo scenarios are embedded from `fixtures/valid/`. The engine does not fetch NIST, CDC, or any other remote data.
- Dependencies are the SDK plus test-only packages locked in `packages.lock.json`: xUnit 2.9.3 (Apache-2.0), xunit.runner.visualstudio 3.1.4 (Apache-2.0), Microsoft.NET.Test.Sdk 18.12.0 (MIT), coverlet.collector 6.0.4 (MIT). The shipped CLI and core projects have no package references. `dotnet list package --vulnerable` reported none.
- This repository is MIT. Fixture text is original. NIST IR 7656 and the CDC storage toolkit are linked from `fixtures/manifest.json` and are not copied.

## Performance

Supported demo: one refrigerator, 20 minutes, truth every minute, logger every 5 minutes. Fixture tree size on disk: 15680 bytes.

| Measurement | Result | Budget |
| --- | ---: | --- |
| 50 engine runs of `power-loss-drill`, including the delay assertion | 29 ms in `PerformanceTests` | under 2000 ms |
| Warm `dotnet run --no-build -- demo` writing a fresh directory | 0.521 s | under 5 s |

The budget is for an interactive rehearsal on a developer machine. It is not a claim about a multi-fridge fleet.

## Residual limitations

- Coefficients and the 2 °C / 8 °C thresholds are demonstration choices. They are not a fit to NIST IR 7656 and they are not a storage standard.
- The model is one lumped temperature. It has no product, no inventory, and no instruction to use or discard anything.
- An unresolved excursion or gap means the scenario ended, not that a real refrigerator was still warm.
- No hardware logger was attached. A real digital data logger is out of scope.
