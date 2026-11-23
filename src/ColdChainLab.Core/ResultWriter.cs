using System.Globalization;

namespace ColdChainLab.Core;

public static class ResultWriter
{
    public static IReadOnlyList<string> Write(SimulationResult result, string outputDirectory, string sourceLabel)
    {
        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        var scenarioId = result.Scenario.ScenarioId;
        var paths = new[]
        {
            Path.Combine(root, scenarioId + ".temperature.csv"),
            Path.Combine(root, scenarioId + ".observations.csv"),
            Path.Combine(root, scenarioId + ".events.json"),
            Path.Combine(root, scenarioId + ".summary.txt"),
        };
        foreach (var path in paths)
        {
            if (!IsInside(root, path))
            {
                throw new InvalidOperationException("Refusing to write outside the output directory.");
            }
        }

        WriteCsv(paths[0], result.Truth);
        WriteCsv(paths[1], result.Observations);
        File.WriteAllText(paths[2], EventReportWriter.Write(result.Report));
        File.WriteAllText(paths[3], SummaryWriter.Write(result, sourceLabel));
        return paths;
    }

    private static void WriteCsv(string path, IReadOnlyList<TemperatureRow> rows)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("minute_min,timestamp_utc,temperature_c");
        foreach (var row in rows)
        {
            writer.Write(row.Minute.ToString("0.######", CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(row.TimestampUtc);
            writer.Write(',');
            writer.WriteLine(row.TemperatureCelsius.ToString("0.000000", CultureInfo.InvariantCulture));
        }
    }

    private static bool IsInside(string root, string path)
    {
        var full = Path.GetFullPath(path);
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.Ordinal);
    }
}
