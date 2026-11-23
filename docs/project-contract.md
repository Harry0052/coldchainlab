# Project contract

Decision carried from `docs/overlap.md`: **proceed**. ColdChainLab is a separate local rehearsal tool. It does not wrap cold-chain excursion engine, the CCE thermal simulator, or cold-chain sentinel.

## User task

A clinic vaccine coordinator or trainer loads one synthetic refrigerator scenario and one logger configuration, runs the drill, and reads whether that configuration would have noticed the simulated failure.

The person can:

1. Run a bundled drill for a single refrigerator.
2. See simulated ground truth and the samples the logger actually recorded, on the same timeline.
3. Read the detection delay, any missed excursion, and any measurement gap as separate findings.
4. Export the scenario and the result together and repeat the drill.

The tool does not alarm a live unit, track inventory, judge product viability, recommend disposition, or control a refrigerator. Every real storage decision stays with the clinic and with official guidance.

## Minimum dependencies

- .NET SDK, pinned in the project file during P02 to a current SDK that `dotnet --version` can install without a paid service.
- xUnit for the test project. No other NuGet packages unless a later phase cannot meet a gate without one.
- JSON scenario files, CSV traces, and plain-text or JSON reports. No database, network, account, or paid API at run time.
- Fixtures are generated in-repo from the documented model and seed. NIST IR 7656 and the CDC Vaccine Storage and Handling Toolkit are linked as method context only. Their figures and datasets are not redistributed. `VCRC` and `Popolo.Core` are not dependencies.

Runtime inputs are local files. The engine must run after a documented SDK install on a normal developer machine.

## Acceptance case

One frozen synthetic power-loss drill, checked by an independent hand calculation.

Model, door closed and cooling off:

`T(t) = T_ambient + (T_initial - T_ambient) * exp(-k * t)`

with `T_initial = 5.0 °C`, `T_ambient = 25.0 °C`, and `k` chosen so `T(12 min) = 8.0 °C`. The demonstration high threshold is 8.0 °C. The continuous trace therefore reaches the threshold at minute 12 and is still above it at minute 20 (`T(15) ≈ 8.677 °C`, `T(20) ≈ 9.746 °C`).

| Check | Expected |
| --- | --- |
| Stable hold at the initial temperature with cooling able to meet that target | Temperature stays at 5.0 °C. No excursion alert. |
| Power-loss trace | Temperature rises monotonically toward 25.0 °C. |
| Logger cadence 5 min, samples kept | First out-of-range sample at minute 15. Reported detection delay is 3 minutes. |
| Same truth, minute-15 and minute-20 samples removed | The excursion is undetected. A gap alert is raised after the configured dropout timeout. The two findings stay distinct. |
| Invalid thermal coefficient or non-positive timing | Rejected with an actionable error before a report is written. |
| Export | Time fields use one stated unit from scenario through result. |

Tolerances for the closed-form trace are exact within ordinary binary rounding (well under 0.01 °C). The acceptance metric is the delay and the missed-versus-gap split on this synthetic fixture, not a fit to a laboratory refrigerator.

## Boundaries that stay in force

- Demonstration thresholds only. They are not a vaccine storage specification.
- Sensor dropout does not change the simulated physical temperature.
- Detection uses samples available at that time. It does not look ahead.
- Outputs keep the simulation label, the threshold, and the units next to the finding.
