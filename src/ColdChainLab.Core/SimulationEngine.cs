using System.Globalization;

namespace ColdChainLab.Core;

public sealed record TemperatureRow(double Minute, string TimestampUtc, double TemperatureCelsius);

public sealed record SimulationResult(
    Scenario Scenario,
    IReadOnlyList<TemperatureRow> Truth,
    IReadOnlyList<TemperatureRow> Observations,
    EventReport Report);

/// <summary>
/// First-order lumped temperature. Each step uses the equilibrium of the active fixture state.
/// Logger dropout removes samples and does not change temperature.
/// </summary>
public static class SimulationEngine
{
    private const double TimeEpsilon = 1e-9;

    public static SimulationResult Run(Scenario scenario)
    {
        var truth = BuildTruth(scenario);
        var observations = BuildObservations(scenario);
        var report = BuildReport(scenario, observations);
        return new SimulationResult(scenario, truth, observations, report);
    }

    public static double TemperatureAt(Scenario scenario, double minute)
    {
        if (minute < 0 || minute > scenario.Time.DurationMinutes + TimeEpsilon)
        {
            throw new ArgumentOutOfRangeException(nameof(minute), "Minute is outside the scenario.");
        }

        var temperature = scenario.Thermal.InitialCelsius;
        var cursor = 0.0;
        foreach (var regime in Regimes(scenario))
        {
            if (minute <= regime.Start + TimeEpsilon)
            {
                break;
            }

            var sliceEnd = Math.Min(minute, regime.End);
            temperature = Step(temperature, regime.CoefficientPerMinute, regime.EquilibriumCelsius, sliceEnd - cursor);
            cursor = sliceEnd;
            if (minute <= regime.End + TimeEpsilon)
            {
                break;
            }
        }

        return temperature;
    }

    private static IReadOnlyList<TemperatureRow> BuildTruth(Scenario scenario)
    {
        var rows = new List<TemperatureRow>();
        for (var minute = 0.0; minute < scenario.Time.DurationMinutes - TimeEpsilon; minute += 1)
        {
            rows.Add(Row(scenario, minute));
        }

        rows.Add(Row(scenario, scenario.Time.DurationMinutes));
        return rows;
    }

    private static IReadOnlyList<TemperatureRow> BuildObservations(Scenario scenario)
    {
        var rows = new List<TemperatureRow>();
        var cadence = scenario.Logger.CadenceMinutes;
        for (var index = 0; ; index++)
        {
            var minute = index * cadence;
            if (minute > scenario.Time.DurationMinutes + TimeEpsilon)
            {
                break;
            }

            if (IsDropped(scenario, minute))
            {
                continue;
            }

            rows.Add(Row(scenario, minute));
        }

        return rows;
    }

    private static EventReport BuildReport(Scenario scenario, IReadOnlyList<TemperatureRow> observations)
    {
        var excursions = TrueExcursions(scenario);
        var gaps = GapAlerts(scenario, observations);
        var segments = EvidenceSegments(scenario, observations);
        var events = new List<ReportedEvent>();
        var usedAlerts = new HashSet<int>();
        var detectionTimes = DetectionTimes(scenario, observations);

        foreach (var excursion in excursions)
        {
            var faultToken = excursion.FaultId ?? "unattributed";
            events.Add(new ReportedEvent(
                $"true-{faultToken}",
                "true-excursion",
                excursion.FaultId,
                excursion.Unresolved ? "unresolved-at-cutoff" : "resolved",
                Round(excursion.StartMinute),
                Round(excursion.EndMinute),
                null,
                null,
                null,
                excursion.ThresholdCelsius));

            var match = -1;
            for (var i = 0; i < detectionTimes.Count; i++)
            {
                if (usedAlerts.Contains(i))
                {
                    continue;
                }

                var detected = detectionTimes[i];
                if (detected + TimeEpsilon >= excursion.StartMinute && detected <= excursion.EndMinute + TimeEpsilon)
                {
                    match = i;
                    break;
                }
            }

            if (match >= 0)
            {
                usedAlerts.Add(match);
                var detected = detectionTimes[match];
                events.Add(new ReportedEvent(
                    $"alert-{faultToken}",
                    "excursion-alert",
                    excursion.FaultId,
                    "detected",
                    null,
                    null,
                    Round(detected),
                    Round(detected - excursion.StartMinute),
                    $"true-{faultToken}",
                    excursion.ThresholdCelsius));
            }
            else
            {
                events.Add(new ReportedEvent(
                    $"missed-{faultToken}",
                    "missed-excursion",
                    excursion.FaultId,
                    "undetected",
                    null,
                    null,
                    null,
                    null,
                    $"true-{faultToken}",
                    excursion.ThresholdCelsius));
            }
        }

        var gapIndex = 0;
        foreach (var gap in gaps)
        {
            gapIndex++;
            events.Add(new ReportedEvent(
                gap.FaultId is null ? $"gap-{gapIndex}" : $"gap-{gap.FaultId}",
                "gap-alert",
                gap.FaultId,
                gap.Closed ? "closed" : "unresolved-at-cutoff",
                Round(gap.StartMinute),
                gap.EndMinute is null ? null : Round(gap.EndMinute.Value),
                Round(gap.DetectedMinute),
                null,
                null,
                null));
        }

        return new EventReport(
            scenario.SchemaVersion,
            scenario.ScenarioId,
            true,
            Limits.Limitation,
            Limits.TimeUnit,
            Limits.TemperatureUnit,
            scenario.Logger.LowCelsius,
            scenario.Logger.HighCelsius,
            segments,
            events);
    }

    private static List<double> DetectionTimes(Scenario scenario, IReadOnlyList<TemperatureRow> observations)
    {
        var times = new List<double>();
        double? runStart = null;
        foreach (var sample in observations)
        {
            var outOfRange = sample.TemperatureCelsius >= scenario.Logger.HighCelsius
                || sample.TemperatureCelsius <= scenario.Logger.LowCelsius;
            if (outOfRange)
            {
                runStart ??= sample.Minute;
            }
            else if (runStart is not null)
            {
                times.Add(runStart.Value);
                runStart = null;
            }
        }

        if (runStart is not null)
        {
            times.Add(runStart.Value);
        }

        return times;
    }

    private static List<ExcursionInterval> TrueExcursions(Scenario scenario)
    {
        var found = new List<ExcursionInterval>();
        ExcursionInterval? open = null;
        var temperature = scenario.Thermal.InitialCelsius;
        if (OutOfRange(scenario, temperature))
        {
            open = Open(scenario, 0, temperature);
        }

        foreach (var regime in Regimes(scenario))
        {
            var hits = Crossings(scenario, temperature, regime);
            foreach (var hit in hits)
            {
                if (hit.Entering)
                {
                    open ??= Open(scenario, regime.Start + hit.DeltaMinutes, hit.ThresholdCelsius);
                }
                else if (open is not null)
                {
                    found.Add(open.Close(regime.Start + hit.DeltaMinutes, unresolved: false));
                    open = null;
                }
            }

            temperature = Step(
                temperature,
                regime.CoefficientPerMinute,
                regime.EquilibriumCelsius,
                regime.End - regime.Start);
        }

        if (open is not null)
        {
            found.Add(open.Close(scenario.Time.DurationMinutes, unresolved: true));
        }

        return found;
    }

    private static ExcursionInterval Open(Scenario scenario, double minute, double temperature)
    {
        var high = temperature >= scenario.Logger.HighCelsius;
        return new ExcursionInterval(
            minute,
            high ? scenario.Logger.HighCelsius : scenario.Logger.LowCelsius,
            ThermalFaultId(scenario, minute));
    }

    private static List<Crossing> Crossings(Scenario scenario, double temperature, Regime regime)
    {
        var length = regime.End - regime.Start;
        var hits = new List<Crossing>();
        Consider(scenario.Logger.HighCelsius, temperature >= scenario.Logger.HighCelsius);
        Consider(scenario.Logger.LowCelsius, temperature <= scenario.Logger.LowCelsius);
        hits.Sort((a, b) => a.DeltaMinutes.CompareTo(b.DeltaMinutes));
        return hits;

        void Consider(double threshold, bool alreadyBeyond)
        {
            if (!TryHit(temperature, regime.CoefficientPerMinute, regime.EquilibriumCelsius, threshold, length, out var delta))
            {
                return;
            }

            var warming = regime.EquilibriumCelsius > temperature;
            var entering = threshold == scenario.Logger.HighCelsius ? warming : !warming;
            if (entering == alreadyBeyond)
            {
                return;
            }

            hits.Add(new Crossing(delta, threshold, entering));
        }
    }

    private static bool TryHit(
        double temperature,
        double coefficientPerMinute,
        double equilibrium,
        double threshold,
        double length,
        out double delta)
    {
        delta = 0;
        var span = temperature - equilibrium;
        if (Math.Abs(span) < 1e-15 || coefficientPerMinute <= 0 || length <= 0)
        {
            return false;
        }

        var ratio = (threshold - equilibrium) / span;
        if (ratio <= 0 || ratio >= 1)
        {
            return false;
        }

        delta = -Math.Log(ratio) / coefficientPerMinute;
        return delta > 1e-10 && delta <= length + TimeEpsilon;
    }

    private static List<GapAlert> GapAlerts(Scenario scenario, IReadOnlyList<TemperatureRow> observations)
    {
        var timeout = scenario.Logger.DropoutTimeoutMinutes;
        var duration = scenario.Time.DurationMinutes;
        var found = new List<GapAlert>();
        if (observations.Count == 0)
        {
            if (timeout <= duration + TimeEpsilon)
            {
                found.Add(new GapAlert(0, timeout, null, DropoutFaultId(scenario, 0, duration), Closed: false));
            }

            return found;
        }

        var previous = 0.0;
        var started = false;
        foreach (var sample in observations)
        {
            if (!started)
            {
                previous = sample.Minute;
                started = true;
                if (sample.Minute >= timeout - TimeEpsilon && sample.Minute > TimeEpsilon)
                {
                    found.Add(new GapAlert(0, timeout, sample.Minute, DropoutFaultId(scenario, 0, sample.Minute), Closed: true));
                }

                continue;
            }

            var silence = sample.Minute - previous;
            if (silence >= timeout - TimeEpsilon)
            {
                found.Add(new GapAlert(
                    previous,
                    previous + timeout,
                    sample.Minute,
                    DropoutFaultId(scenario, previous, sample.Minute),
                    Closed: true));
            }

            previous = sample.Minute;
        }

        if (duration - previous >= timeout - TimeEpsilon)
        {
            found.Add(new GapAlert(
                previous,
                previous + timeout,
                null,
                DropoutFaultId(scenario, previous, duration),
                Closed: false));
        }

        return found;
    }

    private static List<EvidenceSegment> EvidenceSegments(Scenario scenario, IReadOnlyList<TemperatureRow> observations)
    {
        if (observations.Count == 0)
        {
            return [];
        }

        var timeout = scenario.Logger.DropoutTimeoutMinutes;
        var segments = new List<EvidenceSegment>();
        var segment = 1;
        var start = observations[0].Minute;
        var end = start;
        var count = 1;
        for (var i = 1; i < observations.Count; i++)
        {
            var minute = observations[i].Minute;
            if (minute - end >= timeout - TimeEpsilon)
            {
                segments.Add(new EvidenceSegment(segment, Round(start), Round(end), count));
                segment++;
                start = minute;
                count = 1;
            }
            else
            {
                count++;
            }

            end = minute;
        }

        segments.Add(new EvidenceSegment(segment, Round(start), Round(end), count));
        return segments;
    }

    private static string? DropoutFaultId(Scenario scenario, double silentStart, double silentEnd) =>
        scenario.Faults
            .Where(fault => fault.Kind == FaultKind.LoggerDropout
                && fault.StartMinute < silentEnd - TimeEpsilon
                && fault.EndMinute > silentStart + TimeEpsilon)
            .OrderBy(fault => fault.StartMinute)
            .ThenBy(fault => fault.FaultId, StringComparer.Ordinal)
            .Select(fault => fault.FaultId)
            .FirstOrDefault();

    private static string? ThermalFaultId(Scenario scenario, double minute)
    {
        var door = Matching(scenario, FaultKind.DoorOpen, minute);
        if (door is not null)
        {
            return door;
        }

        return Matching(scenario, FaultKind.PowerLoss, minute);
    }

    private static string? Matching(Scenario scenario, FaultKind kind, double minute) =>
        scenario.Faults
            .Where(fault => fault.Kind == kind && minute >= fault.StartMinute - TimeEpsilon && minute <= fault.EndMinute + TimeEpsilon)
            .OrderBy(fault => fault.StartMinute)
            .ThenBy(fault => fault.FaultId, StringComparer.Ordinal)
            .Select(fault => fault.FaultId)
            .FirstOrDefault();

    private static bool IsDropped(Scenario scenario, double minute) =>
        scenario.Faults.Any(fault => fault.Kind == FaultKind.LoggerDropout
            && minute >= fault.StartMinute - TimeEpsilon
            && minute <= fault.EndMinute + TimeEpsilon);

    private static bool OutOfRange(Scenario scenario, double temperature) =>
        temperature >= scenario.Logger.HighCelsius || temperature <= scenario.Logger.LowCelsius;

    private static TemperatureRow Row(Scenario scenario, double minute)
    {
        var clamped = Math.Min(Math.Max(minute, 0), scenario.Time.DurationMinutes);
        var timestamp = scenario.Time.OriginUtc.AddMinutes(clamped).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return new TemperatureRow(Round(clamped), timestamp, TemperatureAt(scenario, clamped));
    }

    private static IReadOnlyList<Regime> Regimes(Scenario scenario)
    {
        var raw = new List<double> { 0, scenario.Time.DurationMinutes };
        foreach (var fault in scenario.Faults)
        {
            if (fault.Kind is FaultKind.PowerLoss or FaultKind.DoorOpen)
            {
                raw.Add(Math.Clamp(fault.StartMinute, 0, scenario.Time.DurationMinutes));
                raw.Add(Math.Clamp(fault.EndMinute, 0, scenario.Time.DurationMinutes));
            }
        }

        raw.Sort();
        var points = new List<double>();
        foreach (var value in raw)
        {
            if (points.Count == 0 || Math.Abs(points[^1] - value) > TimeEpsilon)
            {
                points.Add(value);
            }
        }

        var regimes = new List<Regime>();
        for (var i = 0; i < points.Count - 1; i++)
        {
            var start = points[i];
            var end = points[i + 1];
            var probe = (start + end) / 2;
            var door = scenario.Faults.Any(fault => fault.Kind == FaultKind.DoorOpen
                && probe >= fault.StartMinute && probe <= fault.EndMinute);
            var power = scenario.Faults.Any(fault => fault.Kind == FaultKind.PowerLoss
                && probe >= fault.StartMinute && probe <= fault.EndMinute);
            double coefficient;
            double equilibrium;
            if (door)
            {
                coefficient = scenario.Thermal.DoorCoefficientPerMinute;
                equilibrium = scenario.Thermal.AmbientCelsius;
            }
            else if (power || !scenario.Control.CoolingAvailable)
            {
                coefficient = scenario.Thermal.CoefficientPerMinute;
                equilibrium = scenario.Thermal.AmbientCelsius;
            }
            else
            {
                coefficient = scenario.Thermal.CoefficientPerMinute;
                equilibrium = scenario.Thermal.TargetCelsius;
            }

            regimes.Add(new Regime(start, end, coefficient, equilibrium));
        }

        return regimes;
    }

    private static double Step(double temperature, double coefficientPerMinute, double equilibrium, double deltaMinutes) =>
        equilibrium + ((temperature - equilibrium) * Math.Exp(-coefficientPerMinute * deltaMinutes));

    private static double Round(double value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);

    private readonly record struct Regime(double Start, double End, double CoefficientPerMinute, double EquilibriumCelsius);

    private readonly record struct Crossing(double DeltaMinutes, double ThresholdCelsius, bool Entering);

    private sealed record ExcursionInterval(double StartMinute, double ThresholdCelsius, string? FaultId)
    {
        public ExcursionInterval Close(double endMinute, bool unresolved) =>
            this with { EndMinute = endMinute, Unresolved = unresolved };

        public double EndMinute { get; init; }

        public bool Unresolved { get; init; }
    }

    private readonly record struct GapAlert(
        double StartMinute,
        double DetectedMinute,
        double? EndMinute,
        string? FaultId,
        bool Closed);
}
