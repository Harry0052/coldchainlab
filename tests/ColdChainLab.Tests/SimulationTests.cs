using System.Globalization;
using System.Text.Json;
using ColdChainLab.Cli;
using ColdChainLab.Core;

namespace ColdChainLab.Tests;

public class SimulationTests
{
    private const double Tolerance = 1e-6;

    [Fact]
    public void Power_loss_matches_the_closed_form_and_warms_toward_ambient()
    {
        var scenario = Load("fixtures/valid/power-loss-drill.scenario.json");
        var result = SimulationEngine.Run(scenario);
        var truth = ReadCsv("fixtures/valid/power-loss-drill.temperature.csv");

        Assert.Equal(truth.Count, result.Truth.Count);
        double previous = double.NegativeInfinity;
        for (var i = 0; i < truth.Count; i++)
        {
            Assert.Equal(truth[i].Minute, result.Truth[i].Minute, Tolerance);
            Assert.Equal(truth[i].Temperature, result.Truth[i].TemperatureCelsius, 0.0000005);
            Assert.Equal(ClosedForm(result.Truth[i].Minute), result.Truth[i].TemperatureCelsius, Tolerance);
            Assert.True(result.Truth[i].TemperatureCelsius > previous);
            Assert.True(result.Truth[i].TemperatureCelsius < scenario.Thermal.AmbientCelsius);
            previous = result.Truth[i].TemperatureCelsius;
        }

        Assert.Equal(5, result.Observations.Count);
        Assert.True(TemperatureAtMinute(result.Truth, 10) < 8);
        Assert.Equal(8, TemperatureAtMinute(result.Truth, 12), 0.000001);
        Assert.True(TemperatureAtMinute(result.Truth, 15) > 8);
    }

    [Fact]
    public void Fast_sampling_reports_a_three_minute_delay()
    {
        var result = SimulationEngine.Run(Load("fixtures/valid/power-loss-drill.scenario.json"));
        AssertReport("fixtures/valid/power-loss-drill.events.json", result.Report);
        var alert = Assert.Single(result.Report.Events, item => item.Kind == "excursion-alert");
        Assert.Equal(3, alert.DelayMinutes);
        Assert.Equal(15, alert.DetectedMinute);
        Assert.Equal("power-loss-1", alert.FaultId);
        Assert.DoesNotContain(result.Report.Events, item => item.Kind is "missed-excursion" or "gap-alert");
    }

    [Fact]
    public void Dropout_reports_a_missed_excursion_and_a_separate_gap()
    {
        var kept = SimulationEngine.Run(Load("fixtures/valid/power-loss-drill.scenario.json"));
        var dropped = SimulationEngine.Run(Load("fixtures/valid/power-loss-dropout.scenario.json"));

        Assert.Equal(
            TemperatureAtMinute(kept.Truth, 15),
            TemperatureAtMinute(dropped.Truth, 15),
            Tolerance);
        Assert.Equal([0, 5, 10], dropped.Observations.Select(row => row.Minute).ToArray());
        AssertReport("fixtures/valid/power-loss-dropout.events.json", dropped.Report);

        var missed = Assert.Single(dropped.Report.Events, item => item.Kind == "missed-excursion");
        var gap = Assert.Single(dropped.Report.Events, item => item.Kind == "gap-alert");
        Assert.Equal("power-loss-1", missed.FaultId);
        Assert.Equal("undetected", missed.Status);
        Assert.Equal("logger-dropout-1", gap.FaultId);
        Assert.Equal(20, gap.DetectedMinute);
        Assert.NotEqual(missed.Kind, gap.Kind);
        Assert.DoesNotContain(dropped.Report.Events, item => item.Kind == "excursion-alert");
    }

    [Fact]
    public void Stable_hold_stays_at_the_target_and_raises_no_excursion()
    {
        var result = SimulationEngine.Run(Load("fixtures/valid/stable-hold.scenario.json"));
        Assert.All(result.Truth, row => Assert.Equal(5, row.TemperatureCelsius, Tolerance));
        Assert.All(result.Observations, row => Assert.Equal(5, row.TemperatureCelsius, Tolerance));
        AssertReport("fixtures/valid/stable-hold.events.json", result.Report);
        Assert.Empty(result.Report.Events);
    }

    [Fact]
    public void Slower_sampling_detects_the_same_excursion_later()
    {
        var fast = Load("fixtures/valid/power-loss-drill.scenario.json");
        var slow = fast with { Logger = fast.Logger with { CadenceMinutes = 10 } };
        var fastDelay = AlertDelay(SimulationEngine.Run(fast));
        var slowDelay = AlertDelay(SimulationEngine.Run(slow));

        Assert.Equal(3, fastDelay);
        Assert.Equal(8, slowDelay);
        Assert.True(slowDelay > fastDelay);
    }

    [Fact]
    public void Open_door_warms_faster_than_closed_power_loss()
    {
        var power = Load("fixtures/valid/power-loss-drill.scenario.json");
        var door = power with
        {
            Faults =
            [
                new FaultSpec("door-1", FaultKind.DoorOpen, 0, 10),
            ],
        };

        var closed = SimulationEngine.TemperatureAt(power, 10);
        var open = SimulationEngine.TemperatureAt(door, 10);
        Assert.True(open > closed);
        Assert.True(open < power.Thermal.AmbientCelsius);
        Assert.Equal(ClosedForm(10), closed, Tolerance);
    }

    [Fact]
    public void Logger_recovery_opens_a_second_evidence_segment()
    {
        var scenario = Load("fixtures/valid/power-loss-drill.scenario.json") with
        {
            Time = new TimeSpec(DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture), 25),
            Faults =
            [
                new FaultSpec("power-loss-1", FaultKind.PowerLoss, 0, 25),
                new FaultSpec("logger-dropout-1", FaultKind.LoggerDropout, 15, 20),
            ],
        };

        var result = SimulationEngine.Run(scenario);
        Assert.Equal([0, 5, 10, 25], result.Observations.Select(row => row.Minute).ToArray());
        Assert.Equal(2, result.Report.EvidenceSegments.Count);
        Assert.Equal(1, result.Report.EvidenceSegments[0].Segment);
        Assert.Equal(3, result.Report.EvidenceSegments[0].SampleCount);
        Assert.Equal(2, result.Report.EvidenceSegments[1].Segment);
        Assert.Equal(25, result.Report.EvidenceSegments[1].StartMinute);
        var alert = Assert.Single(result.Report.Events, item => item.Kind == "excursion-alert");
        var gap = Assert.Single(result.Report.Events, item => item.Kind == "gap-alert");
        Assert.Equal(25, alert.DetectedMinute);
        Assert.Equal(13, alert.DelayMinutes);
        Assert.Equal("closed", gap.Status);
        Assert.Equal("logger-dropout-1", gap.FaultId);
        Assert.NotEqual(alert.Kind, gap.Kind);
    }

    [Fact]
    public void Run_command_writes_the_expected_delay_and_rejects_the_invalid_fixture()
    {
        var output = Directory.CreateTempSubdirectory("coldchainlab-run");
        try
        {
            var stdout = new StringWriter();
            var exit = CommandRunner.Run(
                ["run", RepoFiles.Path("fixtures/valid/power-loss-drill.scenario.json"), "--out", output.FullName],
                stdout,
                new StringWriter());

            Assert.Equal(0, exit);
            Assert.Contains("detection-delay-min 3 fault power-loss-1", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains(Limits.Limitation, stdout.ToString(), StringComparison.Ordinal);
            var events = File.ReadAllText(Path.Combine(output.FullName, "power-loss-drill.events.json"));
            Assert.Contains("\"timeUnit\": \"min\"", events, StringComparison.Ordinal);
            Assert.Contains("\"delayMinutes\": 3", events, StringComparison.Ordinal);
            var temperature = File.ReadAllLines(Path.Combine(output.FullName, "power-loss-drill.temperature.csv"));
            Assert.Equal("minute_min,timestamp_utc,temperature_c", temperature[0]);

            var rejectedDir = Directory.CreateTempSubdirectory("coldchainlab-reject");
            try
            {
                var rejectedOut = new StringWriter();
                var rejected = CommandRunner.Run(
                    ["run", RepoFiles.Path("fixtures/invalid/negative-coefficient.scenario.json"), "--out", rejectedDir.FullName],
                    rejectedOut,
                    new StringWriter());
                Assert.Equal(2, rejected);
                Assert.Contains("invalid-thermal-coefficient", rejectedOut.ToString(), StringComparison.Ordinal);
                Assert.Empty(rejectedDir.GetFileSystemInfos());
            }
            finally
            {
                rejectedDir.Delete(true);
            }
        }
        finally
        {
            output.Delete(true);
        }
    }

    private static double AlertDelay(SimulationResult result) =>
        Assert.Single(result.Report.Events, item => item.Kind == "excursion-alert").DelayMinutes!.Value;

    private static double ClosedForm(double minute)
    {
        const double coefficient = 0.013543244125;
        return 25 + ((5 - 25) * Math.Exp(-coefficient * minute));
    }

    private static double TemperatureAtMinute(IReadOnlyList<TemperatureRow> rows, double minute) =>
        rows.Single(row => Math.Abs(row.Minute - minute) < Tolerance).TemperatureCelsius;

    private static Scenario Load(string relative) =>
        ScenarioValidator.Read(File.ReadAllText(RepoFiles.Path(relative))).Scenario!;

    private static List<(double Minute, double Temperature)> ReadCsv(string relative) =>
        File.ReadAllLines(RepoFiles.Path(relative))
            .Skip(1)
            .Where(line => line.Length > 0)
            .Select(line => line.Split(','))
            .Select(parts => (
                double.Parse(parts[0], CultureInfo.InvariantCulture),
                double.Parse(parts[2], CultureInfo.InvariantCulture)))
            .ToList();

    private static void AssertReport(string relativeExpected, EventReport actual)
    {
        using var expected = JsonDocument.Parse(File.ReadAllText(RepoFiles.Path(relativeExpected)));
        using var produced = JsonDocument.Parse(EventReportWriter.Write(actual));
        var expectedRoot = expected.RootElement;
        var producedRoot = produced.RootElement;
        Assert.Equal(expectedRoot.GetProperty("scenarioId").GetString(), producedRoot.GetProperty("scenarioId").GetString());
        Assert.Equal("min", producedRoot.GetProperty("timeUnit").GetString());
        Assert.Equal("C", producedRoot.GetProperty("temperatureUnit").GetString());
        Assert.True(producedRoot.GetProperty("simulation").GetBoolean());
        Assert.Equal(Limits.Limitation, producedRoot.GetProperty("limitation").GetString());

        var expectedSegments = expectedRoot.GetProperty("evidenceSegments").EnumerateArray().ToArray();
        var producedSegments = producedRoot.GetProperty("evidenceSegments").EnumerateArray().ToArray();
        Assert.Equal(expectedSegments.Length, producedSegments.Length);
        for (var i = 0; i < expectedSegments.Length; i++)
        {
            Assert.Equal(expectedSegments[i].GetProperty("segment").GetInt32(), producedSegments[i].GetProperty("segment").GetInt32());
            Assert.Equal(expectedSegments[i].GetProperty("sampleCount").GetInt32(), producedSegments[i].GetProperty("sampleCount").GetInt32());
            Assert.Equal(expectedSegments[i].GetProperty("startMinute").GetDouble(), producedSegments[i].GetProperty("startMinute").GetDouble(), Tolerance);
            Assert.Equal(expectedSegments[i].GetProperty("endMinute").GetDouble(), producedSegments[i].GetProperty("endMinute").GetDouble(), Tolerance);
        }

        var expectedEvents = expectedRoot.GetProperty("events").EnumerateArray().ToArray();
        var producedEvents = producedRoot.GetProperty("events").EnumerateArray().ToArray();
        Assert.Equal(expectedEvents.Length, producedEvents.Length);
        for (var i = 0; i < expectedEvents.Length; i++)
        {
            Assert.Equal(expectedEvents[i].GetProperty("eventId").GetString(), producedEvents[i].GetProperty("eventId").GetString());
            Assert.Equal(expectedEvents[i].GetProperty("kind").GetString(), producedEvents[i].GetProperty("kind").GetString());
            Assert.Equal(expectedEvents[i].GetProperty("status").GetString(), producedEvents[i].GetProperty("status").GetString());
            AssertOptionalString(expectedEvents[i], producedEvents[i], "faultId");
            AssertOptionalString(expectedEvents[i], producedEvents[i], "matchedTrueEventId");
            AssertOptionalDouble(expectedEvents[i], producedEvents[i], "startMinute");
            AssertOptionalDouble(expectedEvents[i], producedEvents[i], "endMinute");
            AssertOptionalDouble(expectedEvents[i], producedEvents[i], "detectedMinute");
            AssertOptionalDouble(expectedEvents[i], producedEvents[i], "delayMinutes");
            AssertOptionalDouble(expectedEvents[i], producedEvents[i], "thresholdCelsius");
        }
    }

    private static void AssertOptionalString(JsonElement expected, JsonElement actual, string name)
    {
        var has = expected.TryGetProperty(name, out var expectedValue) && expectedValue.ValueKind != JsonValueKind.Null;
        var actualHas = actual.TryGetProperty(name, out var actualValue) && actualValue.ValueKind != JsonValueKind.Null;
        Assert.Equal(has, actualHas);
        if (has)
        {
            Assert.Equal(expectedValue.GetString(), actualValue.GetString());
        }
    }

    private static void AssertOptionalDouble(JsonElement expected, JsonElement actual, string name)
    {
        var has = expected.TryGetProperty(name, out var expectedValue) && expectedValue.ValueKind == JsonValueKind.Number;
        var actualHas = actual.TryGetProperty(name, out var actualValue) && actualValue.ValueKind == JsonValueKind.Number;
        Assert.Equal(has, actualHas);
        if (has)
        {
            Assert.Equal(expectedValue.GetDouble(), actualValue.GetDouble(), Tolerance);
        }
    }
}
