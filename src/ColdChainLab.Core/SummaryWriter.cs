using System.Globalization;
using System.Text;

namespace ColdChainLab.Core;

public static class SummaryWriter
{
    public static string Write(SimulationResult result, string sourceLabel)
    {
        var report = result.Report;
        var builder = new StringBuilder();
        builder.AppendLine("ColdChainLab drill report");
        builder.AppendLine("simulation-status: completed");
        builder.AppendLine($"limitation: {report.Limitation}");
        builder.AppendLine($"scenario: {result.Scenario.ScenarioId}");
        builder.AppendLine($"source: {sourceLabel}");
        builder.AppendLine($"schema: {report.SchemaVersion}");
        builder.AppendLine($"time-unit: {report.TimeUnit}");
        builder.AppendLine($"temperature-unit: {report.TemperatureUnit}");
        builder.AppendLine(
            $"thresholds: low {Format(report.LowCelsius)} C, high {Format(report.HighCelsius)} C (demonstration values, not a storage specification)");
        builder.AppendLine("provenance: synthetic temperatures generated from the scenario. No remote data and no study dataset.");
        builder.AppendLine();
        builder.AppendLine("Findings");
        var alerts = report.Events.Where(item => item.Kind == "excursion-alert").ToArray();
        var missed = report.Events.Where(item => item.Kind == "missed-excursion").ToArray();
        var gaps = report.Events.Where(item => item.Kind == "gap-alert").ToArray();
        if (alerts.Length == 0)
        {
            builder.AppendLine("detection delay: none");
        }

        foreach (var alert in alerts)
        {
            builder.AppendLine(
                $"detection delay: {Format(alert.DelayMinutes ?? 0)} min, fault {alert.FaultId}, detected at minute {Format(alert.DetectedMinute ?? 0)}, matches {alert.MatchedTrueEventId}");
        }

        if (missed.Length == 0)
        {
            builder.AppendLine("missed excursions: 0");
        }

        foreach (var item in missed)
        {
            var truth = report.Events.First(candidate => candidate.EventId == item.MatchedTrueEventId);
            builder.AppendLine(
                $"missed excursion: fault {item.FaultId}, true onset minute {Format(truth.StartMinute ?? 0)}, status {item.Status}. This is separate from any gap alert.");
        }

        if (gaps.Length == 0)
        {
            builder.AppendLine("measurement gaps: 0");
        }

        foreach (var gap in gaps)
        {
            builder.AppendLine(
                $"measurement gap: fault {gap.FaultId ?? "none"}, from minute {Format(gap.StartMinute ?? 0)}, alert at minute {Format(gap.DetectedMinute ?? 0)}, status {gap.Status}. No observed temperature is invented inside the gap.");
        }

        if (report.Events.Any(item => item.Kind == "true-excursion" && item.Status == "unresolved-at-cutoff"))
        {
            builder.AppendLine("incomplete: a true excursion was still past the demonstration threshold at cutoff, so its status is unresolved-at-cutoff.");
        }

        if (gaps.Any(item => item.Status == "unresolved-at-cutoff"))
        {
            builder.AppendLine("recovery: no sample arrived after the gap. A later sample would open a new evidence segment. It would not erase the gap or fill in the missing temperatures.");
        }
        else if (report.EvidenceSegments.Count > 1)
        {
            var resumed = report.EvidenceSegments[^1];
            builder.AppendLine(
                $"recovery: evidence segment {resumed.Segment} starts at minute {Format(resumed.StartMinute)}. The gap alert stays a separate finding.");
        }

        builder.AppendLine();
        builder.AppendLine("Timeline");
        builder.AppendLine("minute_min  truth_c   observed_c  evidence         finding");
        var observed = result.Observations.ToDictionary(row => row.Minute);
        foreach (var row in result.Truth)
        {
            var hasSample = observed.TryGetValue(row.Minute, out var sample);
            var evidence = hasSample ? "observed" : EvidenceAt(report, row.Minute);
            var observedText = hasSample ? sample!.TemperatureCelsius.ToString("0.000000", CultureInfo.InvariantCulture) : "-";
            builder.AppendLine(
                $"{row.Minute.ToString("0.######", CultureInfo.InvariantCulture),10}  {row.TemperatureCelsius.ToString("0.000000", CultureInfo.InvariantCulture),8}  {observedText,10}  {evidence,-15}  {FindingAt(report, row.Minute)}");
        }

        builder.AppendLine();
        builder.AppendLine("evidence `gap` marks a measurement gap. `-` means the logger has no sample at that minute.");
        builder.AppendLine("This table is a simulated rehearsal. It is not a vaccine viability, disposition, or compliance judgment.");
        return builder.ToString();
    }

    private static string EvidenceAt(EventReport report, double minute)
    {
        foreach (var gap in report.Events.Where(item => item.Kind == "gap-alert"))
        {
            var start = gap.StartMinute ?? 0;
            var end = gap.EndMinute ?? gap.DetectedMinute ?? start;
            var afterStart = minute > start + 1e-9;
            var beforeEnd = gap.EndMinute is null
                ? minute <= end + 1e-9
                : minute < end - 1e-9;
            if (afterStart && beforeEnd)
            {
                return "gap";
            }
        }

        return "between-samples";
    }

    private static string FindingAt(EventReport report, double minute)
    {
        var notes = new List<string>();
        foreach (var item in report.Events)
        {
            if (item.Kind == "true-excursion" && Nearly(item.StartMinute, minute))
            {
                notes.Add($"true-excursion {item.FaultId} onset");
            }

            if (item.Kind == "excursion-alert" && Nearly(item.DetectedMinute, minute))
            {
                notes.Add($"excursion-alert {item.FaultId} delay {Format(item.DelayMinutes ?? 0)} min");
            }

            if (item.Kind == "gap-alert" && Nearly(item.DetectedMinute, minute))
            {
                notes.Add($"gap-alert {item.FaultId}");
            }
        }

        return notes.Count == 0 ? "" : string.Join("; ", notes);
    }

    private static bool Nearly(double? value, double minute) =>
        value is not null && Math.Abs(value.Value - minute) < 1e-6;

    private static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
