using System.Text.Json;
using System.Text.Json.Serialization;

namespace ColdChainLab.Core;

public sealed record EvidenceSegment(int Segment, double StartMinute, double EndMinute, int SampleCount);

public sealed record ReportedEvent(
    string EventId,
    string Kind,
    string? FaultId,
    string Status,
    double? StartMinute,
    double? EndMinute,
    double? DetectedMinute,
    double? DelayMinutes,
    string? MatchedTrueEventId,
    double? ThresholdCelsius);

public sealed record EventReport(
    string SchemaVersion,
    string ScenarioId,
    bool Simulation,
    string Limitation,
    string TimeUnit,
    string TemperatureUnit,
    double LowCelsius,
    double HighCelsius,
    IReadOnlyList<EvidenceSegment> EvidenceSegments,
    IReadOnlyList<ReportedEvent> Events);

public static class EventReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static string Write(EventReport report)
    {
        if (report.TimeUnit != Limits.TimeUnit || report.TemperatureUnit != Limits.TemperatureUnit)
        {
            throw new ArgumentException(
                $"Event reports must use time unit \"{Limits.TimeUnit}\" and temperature unit \"{Limits.TemperatureUnit}\".");
        }

        return JsonSerializer.Serialize(report, Options) + Environment.NewLine;
    }
}
