# Fixture oracle

These numbers are the hand calculation for schema version 1. They are synthetic. They are not a fit to a laboratory refrigerator and they are not a storage specification.

Time is in minutes from `2026-01-01T00:00:00Z`. Temperature is in °C. A fault interval is closed: a sample at minute `t` belongs to the fault when `startMinute <= t <= endMinute`.

## Stable hold

`stable-hold` has cooling available, mode `hold-target`, initial temperature equal to the target (5.0 °C), and no faults. While the controller can hold the target, the analytical solution is

`T(t) = 5.0 °C`

for the whole run. The trace does not relax toward the 25.0 °C ambient. Every 5-minute sample is 5.0 °C, which is inside 2.0 °C to 8.0 °C. Expected events: none. Expected file: `fixtures/valid/stable-hold.events.json`.

## Power loss

`power-loss-1` runs from minute 0 through minute 20, so cooling is off for the whole drill. With the door closed the temperature relaxes toward ambient:

`T(t) = 25 + (5 - 25) * exp(-k t)`

`k = -ln((8 - 25) / (5 - 25)) / 12 = -ln(0.85) / 12 = 0.013543244125` per minute.

That choice of `k` makes `T(12) = 8.0` °C. The high demonstration threshold is 8.0 °C and is inclusive, so the true excursion starts at minute 12. It is still above the threshold at minute 20 (`T(20) = 9.745609` °C), and the run ends there, so the true excursion is `unresolved-at-cutoff`.

Check points, rounded to 6 decimal places in the CSV:

| minute | T (°C) | At or above 8 °C |
| ---: | ---: | --- |
| 0 | 5.000000 | no |
| 5 | 6.309487 | no |
| 10 | 7.533237 | no |
| 12 | 8.000000 | yes, onset |
| 15 | 8.676862 | yes |
| 20 | 9.745609 | yes |

The series increases at every whole minute from 0 through 20, toward 25 °C. `doorCoefficientPerMinute` is 0.08 and is unused while the door stays closed. It is a synthetic door-open rate for a later drill, not a measured value.

Logger cadence is 5 minutes, so the kept samples are minutes 0, 5, 10, 15, and 20. The first sample at or above 8.0 °C is minute 15. Detection delay is `15 - 12 = 3` minutes. No sample is missing, and the dropout timeout is 10 minutes, so there is no gap alert. Both the true excursion and the alert carry `faultId` `power-loss-1`. Expected file: `fixtures/valid/power-loss-drill.events.json`.

## Dropout replay

`power-loss-dropout` uses the same physical trace. `logger-dropout-1` covers minutes 15 through 20, so those logger samples are absent. The temperature CSV still contains them. Observations are minutes 0, 5, and 10, all below 8.0 °C.

The true excursion is unchanged and undetected: kind `missed-excursion`, fault `power-loss-1`. Separately, the last observation is minute 10 and `dropoutTimeoutMinutes` is 10, so a `gap-alert` is due at minute 20. That alert's fault id is `logger-dropout-1`. The two findings do not share an event kind or a fault id. The gap stays open at cutoff because no later sample arrives, and the only evidence segment is minutes 0 through 10 (3 samples). Expected file: `fixtures/valid/power-loss-dropout.events.json`.

A sample after the gap would open evidence segment 2. It would not erase the gap alert or back-fill the missed onset. No recovery sample is in this fixture.

## Invalid file

`fixtures/invalid/negative-coefficient.scenario.json` sets `thermal.coefficientPerMinute` to `-0.2`. Validation must reject it with category `invalid-thermal-coefficient` and must not write a result report. The coefficient has to be a finite number greater than 0 and at most 5 per minute.

## What is not claimed

NIST IR 7656 and the CDC Vaccine Storage and Handling Toolkit are method context only. None of their measurements are in these files. Thresholds here are demonstration values for the drill.
