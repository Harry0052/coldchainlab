namespace ColdChainLab.Core;

public static class ScenarioFile
{
    public static ScenarioValidation ReadPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ScenarioValidation.Reject(
            [
                new ContractError(
                    ErrorCategory.UnreadableInput,
                    "scenario",
                    "Provide a path to a scenario JSON file."),
            ]);
        }

        FileInfo info;
        try
        {
            info = new FileInfo(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Unreadable(path, ex.Message);
        }

        if (!info.Exists)
        {
            return Unreadable(path, "The file does not exist.");
        }

        if ((info.Attributes & FileAttributes.Directory) != 0)
        {
            return Unreadable(path, "The path is a directory, not a scenario file.");
        }

        if (info.Length > Limits.MaxScenarioBytes)
        {
            return ScenarioValidation.Reject(
            [
                new ContractError(
                    ErrorCategory.InputTooLarge,
                    "scenario",
                    $"Scenario file is {info.Length} bytes. The maximum is {Limits.MaxScenarioBytes} bytes."),
            ]);
        }

        string json;
        try
        {
            json = File.ReadAllText(info.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Unreadable(path, ex.Message);
        }

        return ScenarioValidator.Read(json, checked((int)info.Length));
    }

    private static ScenarioValidation Unreadable(string path, string detail) =>
        ScenarioValidation.Reject(
        [
            new ContractError(
                ErrorCategory.UnreadableInput,
                "scenario",
                $"Could not read scenario file \"{path}\". {detail}"),
        ]);
}
