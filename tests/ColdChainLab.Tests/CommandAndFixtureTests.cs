using ColdChainLab.Cli;
using ColdChainLab.Core;

namespace ColdChainLab.Tests;

public class CommandAndFixtureTests
{
    [Fact]
    public void Validate_command_accepts_the_valid_fixture()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CommandRunner.Run(
            ["validate", RepoFiles.Path("fixtures/valid/power-loss-drill.scenario.json")],
            stdout,
            stderr);

        Assert.Equal(0, exit);
        Assert.Contains("accepted power-loss-drill", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains(Limits.Limitation, stdout.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public void Validate_command_rejects_the_invalid_fixture()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CommandRunner.Run(
            ["validate", "--json", RepoFiles.Path("fixtures/invalid/negative-coefficient.scenario.json")],
            stdout,
            stderr);

        Assert.Equal(2, exit);
        Assert.Contains("\"status\": \"rejected\"", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("invalid-thermal-coefficient", stdout.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public void Help_exits_zero_and_names_the_time_unit()
    {
        var stdout = new StringWriter();
        var exit = CommandRunner.Run(["--help"], stdout, new StringWriter());

        Assert.Equal(0, exit);
        Assert.Contains("minutes", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Power_loss_trace_matches_the_hand_calculation()
    {
        var rows = ReadCsv(RepoFiles.Path("fixtures/valid/power-loss-drill.temperature.csv"));
        Assert.Equal("minute_min", rows[0][0]);
        Assert.Equal("temperature_c", rows[0][2]);

        var byMinute = rows.Skip(1).ToDictionary(row => int.Parse(row[0], System.Globalization.CultureInfo.InvariantCulture), row => row);
        Assert.Equal(8.0, double.Parse(byMinute[12][2], System.Globalization.CultureInfo.InvariantCulture), 0.000001);
        Assert.True(double.Parse(byMinute[10][2], System.Globalization.CultureInfo.InvariantCulture) < 8);
        Assert.True(double.Parse(byMinute[15][2], System.Globalization.CultureInfo.InvariantCulture) > 8);

        double previous = double.NegativeInfinity;
        foreach (var minute in Enumerable.Range(0, 21))
        {
            var temperature = double.Parse(byMinute[minute][2], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(temperature > previous);
            previous = temperature;
        }
    }

    [Fact]
    public void Dropout_observations_omit_minutes_15_and_20_while_truth_keeps_them()
    {
        var observations = ReadCsv(RepoFiles.Path("fixtures/valid/power-loss-dropout.observations.csv"))
            .Skip(1)
            .Select(row => int.Parse(row[0], System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        var truth = ReadCsv(RepoFiles.Path("fixtures/valid/power-loss-dropout.temperature.csv"))
            .Skip(1)
            .Select(row => int.Parse(row[0], System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal([0, 5, 10], observations);
        Assert.Contains(15, truth);
        Assert.Contains(20, truth);
    }

    [Fact]
    public void Event_writer_keeps_units_and_fault_identity()
    {
        var json = EventReportWriter.Write(new EventReport(
            Limits.SchemaVersion,
            "power-loss-drill",
            true,
            Limits.Limitation,
            Limits.TimeUnit,
            Limits.TemperatureUnit,
            2,
            8,
            [new EvidenceSegment(1, 0, 20, 5)],
            [
                new ReportedEvent(
                    "alert-power-loss-1",
                    "excursion-alert",
                    "power-loss-1",
                    "detected",
                    null,
                    null,
                    15,
                    3,
                    "true-power-loss-1",
                    8),
            ]));

        Assert.Contains("\"timeUnit\": \"min\"", json, StringComparison.Ordinal);
        Assert.Contains("\"temperatureUnit\": \"C\"", json, StringComparison.Ordinal);
        Assert.Contains("\"faultId\": \"power-loss-1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"delayMinutes\": 3", json, StringComparison.Ordinal);
        Assert.Contains(Limits.Limitation, json, StringComparison.Ordinal);
    }

    private static string[][] ReadCsv(string path) =>
        File.ReadAllLines(path)
            .Where(line => line.Length > 0)
            .Select(line => line.Split(','))
            .ToArray();
}

public static class RepoFiles
{
    public static string Path(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "ColdChainLab.slnx")))
            {
                return System.IO.Path.Combine(dir.FullName, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root from the test output directory.");
    }
}
