# Overlap review

Access date: 2026-11-23. ColdChainLab is an empty public repository (`Harry0052/coldchainlab`) with a README title and an MIT license. This review asks whether a clinic monitoring-configuration drill already exists.

**User.** A clinic vaccine coordinator or trainer checking whether a proposed logger setup would notice a simulated storage failure.

**Hypothesis.** Comparing a known simulated failure with the alerts that logger configuration actually emits can show monitoring blind spots during training.

**Acceptance metric held constant.** One synthetic power-loss trace first reaches 8.0 °C at minute 12 and stays above it through minute 20. Samples every five minutes must report detection at minute 15, a delay of three minutes. A second replay that drops the minute-15 and minute-20 samples must report that excursion as undetected and must raise a separate gap alert. A constant 5.0 °C trace must not raise an excursion alert. The result is a training score under synthetic assumptions, not a product-viability or disposition judgment.

**Shared fixture used for runs.** First-order relaxation during power loss, door closed:

`T(t) = 25 + (5 - 25) * exp(-k t)` with `t` in minutes and `k = -ln((8 - 25) / (5 - 25)) / 12 = 0.01354324 /min`, so `T(12) = 8.000 °C`.

| t (min) | T (°C) | Above 8 °C |
| ---: | ---: | --- |
| 0 | 5.000 | no |
| 5 | 6.309 | no |
| 10 | 7.533 | no |
| 12 | 8.000 | onset |
| 15 | 8.677 | yes |
| 20 | 9.746 | yes |
| 25 | 10.744 | yes |

Fast samples: minutes 0, 5, 10, 15, 20. Dropout samples: minutes 0, 5, 10. A recovery sample at minute 25 was added only where a tool can notice a gap solely when a later point arrives.

## Capability matrix

| Candidate | What it already does | Clinic drill (one fridge, ground truth vs logger, 3-minute delay, missed excursion distinct from gap) | Evidence |
| --- | --- | --- | --- |
| NIST IR 7656, *Thermal Analysis of Refrigeration Systems Used for Vaccine Storage* | Measured household and dormitory refrigerators, power-outage warming, and four electronic data loggers. Vial temperature often differed from air temperature. | Measurement study, not software. No configurable logger rehearsal and no alert-delay score. | Documentation. Fetched 2026-11-23. |
| CDC Vaccine Storage and Handling Toolkit (page updated 2026-07-14) | Official monitoring and excursion guidance for providers. Digital data loggers, excursion response, and a warning not to treat vendor “CDC-compliant” claims as endorsement. | Policy source, not a simulator. Must not be copied into drill labels as a clinical verdict. | Documentation. Fetched 2026-11-23. |
| [VB-Builds/cold-chain-excursion-engine](https://github.com/VB-Builds/cold-chain-excursion-engine) 0.1.0, commit `f5dd4dc7b39220e33a44f06083e9d74725c0b751` (2026-11-12), MIT | Shipment lumped thermal model, logger gaps, excursion debounce, data-gap alarms, MKT, stability budgets, early warning. Fleet metrics vary with 5/15/30-minute sampling. | Does not meet the metric. See probe. | Observed run. |
| [phoenixgh-org/ems-data-simulator](https://github.com/phoenixgh-org/ems-data-simulator) commit `7ca666c183f3744d82bd9cc881b1453b0465658a` (2026-08-20), MIT | Physics-based refrigerator telemetry for the CCE data-delivery spec. Power loss, stuck door, compressor and refrigerant faults. WHO PQS-style alarms. | Does not meet the metric. See probe. Sample interval is capped at 900 s by that schema. | Observed run of `AlarmGenerator`. |
| [jubjub-cpu/coldchain-sentinel](https://github.com/jubjub-cpu/coldchain-sentinel) commit `ad0b8af3acf40cc10e201bff2485515baa6767cb` (2026-07-18), MIT | Browser rules for synthetic shipment telemetry. Persistence, suppressed spikes, gap alerts, human review, explicit non-disposition limits. | Does not meet the metric. No thermal model and no ground-truth delay. | Observed run of `analyzeTelemetry`. |
| [ibm-cloud-architecture/vaccine-reefer-simulator](https://github.com/ibm-cloud-architecture/vaccine-reefer-simulator) commit `25359f69a541b2b9290e8510f6afccb736d688ab` (2021-03-23), license unset | Python reefer telemetry (`tempgrowth` toward ambient) for a Kafka vaccine-monitoring demo. | Shipping-container generator. No detection-delay score against a clinic logger. | Documentation. Not executed: license field is null. |
| [orhaugh/coldchain](https://github.com/orhaugh/coldchain) commit `a47e6a992ce2c8bf36a1b7c2dfb6a3764c5281a9` (2026-11-18), Apache-2.0 | Refrigerated-freight state machines. A `silence` tape expects one silence alert and no excursion. | Freight journeys, not a single clinic refrigerator thermal drill. | Documentation. Not executed: requires the clink C++ toolchain. |
| [utoker/coldtrace](https://github.com/utoker/coldtrace) commit `15c141fb517755aada1c4236211a252ac2269ff9` (2026-11-09), MIT, archived | Dashboard plus a keyboard simulator (spike to 12 °C, power outage, offline). Alerts for excursion and connectivity. | No continuous thermal onset and no scored detection delay. Hosted demo is down. | Documentation. Not executed. |
| NuGet `VCRC` 4.0.0 (updated 2026-05-28) | Vapor-compression refrigeration-cycle analysis. Targets .NET 10 and .NET Standard 2.0. | Thermodynamic cycle tool. No logger, excursion, or gap scoring. | Documentation. Gallery page does not state an SPDX license in the fetched text. |
| NuGet `Popolo.Core` 3.1.5 | Building thermal and HVAC equipment models. | Not a vaccine-logger coverage drill. | Documentation from the package summary. |

GitHub repository search `vaccine refrigerator temperature simulation logger` on 2026-11-23 returned `total_count: 0`. The candidates above came from web search plus the repository and package pages.

## Probe outcomes

Commands ran on this machine (Python 3.12.3, Node 22.23.4). Third-party trees stayed in `/tmp/p01-probes` and are not part of this repository. Pandas 3.1.1 and Pydantic were installed only for those probes.

### Cold-chain excursion engine (observed)

```text
PYTHONPATH=/tmp/p01-probes/ccee python3  # Monitor + ProductProfile("drill-2-8", 2.0, 8.0)
```

Version string `0.1.0`. Early warning disabled so only the excursion and gap rules were in play.

| Replay | Settings | Observed |
| --- | --- | --- |
| Fast 0,5,10,15,20 | default `debounce_samples=2`, `max_gap_min=30`, `gap_alarm_min=60` | Excursion event starts at minute 15 (delay 3 versus minute 12) but the excursion **alarm is emitted at minute 20**. Also emits `rate_of_change` at minute 10 and a `budget` critical alarm at minute 15 because a profile with no stability bands treats any out-of-range minute as an exhausted allowance. |
| Fast | `debounce_samples=1` | Excursion alarm at minute 15. Still emits rate and budget alarms. No ground-truth onset field. |
| Dropout 0,5,10 | default | No excursion and no `data_gap` alarm. End-of-record silence is invisible. |
| Dropout plus minute 25 | default | Still no excursion (one out-of-range sample, debounce 2) and no gap (15 min is under the 30 min gap threshold and the 60 min gap alarm). |
| Dropout plus minute 25 | `debounce_samples=1`, `max_gap_min=10`, `gap_alarm_min=10` | Gap alarm **and** an excursion at minute 25 flagged `onset_unobserved`. That is a late detection, not an undetected excursion kept separate from the gap. Delay versus minute 12 is 13 minutes. |
| Constant 5.0 °C | `debounce_samples=1` | No events and no alarms. This part matches. |

The engine’s own README (same commit) reports fleet recall across shipments that use Open-Meteo / ERA5 weather (CC BY 4.0) and says no real logger file has been analysed. That fleet table is a different artifact from this single-fridge delay.

### CCE thermal simulator alarm generator (observed)

```text
PYTHONPATH=/tmp/p01-probes/ems python3
from ccesim.simulator.events import AlarmGenerator
```

`derive_alarms` on the fast series and on the dropout series, power unavailable, no door events, `interval_s=300`, returned `ALRM=None` at every sample. Source constants at this commit: `HEAT` requires `TVC > 8 °C` for 36000 s, `POWR` requires 86400 s, `DOOR` requires 300 s. A 20-minute drill cannot open those alarms. `validate_sample_interval` also rejects intervals above 900 s, and the device layer’s normal cadence is 600 or 900 s, so the five-minute proof cadence is outside the interoperability contract this simulator is built to emit.

### Cold-chain sentinel (observed)

```text
node --input-type=module  # analyzeTelemetry from assets/telemetry-engine.mjs @ ad0b8af3
```

Default policy is `tempMax: 8`, `persistence: 3`, `maxGapMinutes: 15`.

| Replay | Observed |
| --- | --- |
| Fast, default | Status `observe`. The two breach samples are a suppressed spike. No excursion alert and no delay. |
| Fast, `persistence: 1` | Status `incident`. One temperature alert. No field for “delay = 3 min from minute 12”. |
| Dropout 0,5,10 | Status `stable`. No gap and no missed-excursion record. The engine only sees delivered readings. |
| Dropout plus minute 25, default | Status `observe`. One suppressed spike. Gap of 15 minutes is not greater than `maxGapMinutes`, so no gap alert. |
| Dropout plus minute 25, `persistence: 1`, `maxGapMinutes: 10` | Both a temperature alert and a gap alert, but the temperature alert is a detection at the recovery sample, not a missed true excursion. |
| Constant 5.0 °C, `persistence: 1` | Status `stable`. No alerts. |

### Checks that stayed on documentation

- NIST IR 7656 ([publication page](https://www.nist.gov/publications/thermal-analysis-refrigeration-systems-used-vaccine-storage-0), report 7656) and the related small-unit study NIST IR 7900 ([PDF](https://nvlpubs.nist.gov/nistpubs/ir/2012/NIST.IR.7900.pdf)) document that closed refrigerators warm after power loss and that logger placement matters. Neither ships a trainer. ColdChainLab will not copy their tables, figures, or thermocouple series. Thermal coefficients in the drill stay synthetic.
- CDC toolkit ([storage and handling](https://www.cdc.gov/vaccines/hcp/storage-handling/index.html), fetched text dated 2026-07-14) and the Pink Book storage chapter describe digital data loggers and excursion response. Drill copy must say the run is simulated and must not instruct the user to discard, use, or disposition any product.
- IBM reefer simulator README describes `tempgrowth` toward ambient and a Kafka control API. It does not score logger cadence against a known onset. No license is set on the repository object, so the comparison stopped at the README.
- `orhaugh/coldchain` separates silence from excursion on synthetic freight tapes. That split is relevant prior art for classification, and it is not a refrigerator model or the three-minute clinic case. Clink is not installed here.
- `coldtrace` is an archived multi-service demo. Its simulator sets a device to 12 °C from a keypress. It does not compute detection delay from a continuous onset.
- `VCRC` 4.0.0 analyses vapor-compression cycles. It does not ingest a logger cadence. No run.

## Remaining uncertainties

- Cold-chain excursion engine’s event **start** on the fast replay is minute 15, which matches the delay number, while the alarm the operator would see is one sample later and the dropout replay never produces the required pair of findings. Retuning debounce and gap limits still merges the missed event into a detected excursion. That is not partial credit for the acceptance case.
- `orhaugh/coldchain` and `coldtrace` were not executed. Their READMEs are specific enough that a hidden clinic-fridge mode is unlikely, and both would still need a new thermal fixture and a new oracle to meet this metric.
- The IBM repository has no SPDX license on the GitHub license field. Do not copy its code.
- `VCRC`’s SPDX id was not on the gallery page that was fetched. It is out of scope either way.
- WHO PQS E003 / E006 durations showed up inside the CCE simulator (10-hour heat, 24-hour power). Those are equipment-qualification thresholds, not the training metric, and the PQS PDF is not redistributed here.

## Decision: proceed

No existing workflow meets the acceptance case. The closest MIT tools should stay references, not dependencies:

- Contribute a “clinic drill” mode to cold-chain excursion engine only if the product were a shipment monitor. It is not. Its budgets, MKT, weather archive, and GDP-style report answer a different question, and its observed output fails this metric. Pulling it in would also imply product-stability math that this MVP excludes.
- Contribute detection-delay scoring to ems-data-simulator only if the product were CCE interoperability telemetry. Its alarms are multi-hour PQS conditions and its sample interval cannot express the proof cadence.
- Cold-chain sentinel already separates gaps from temperature runs and states that a person owns disposition. It has no refrigerator model and, on this fixture, suppresses the excursion the drill must score.

Build a small local rehearsal tool: one synthetic refrigerator, one logger configuration, three faults (power loss, abrupt door opening, logger dropout), an overlay of ground truth and observed samples, a detection delay, separate missed-event and gap findings, and an export of the scenario with the result. Use original synthetic series. Link NIST and CDC as method context. Do not present a clinical or compliance verdict.
