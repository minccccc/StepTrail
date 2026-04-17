namespace StepTrail.TestLab;

public static class LabScenarioNames
{
    public const string HappyPath = "happy-path";
    public const string FailThenRecover = "fail-then-recover";
    public const string PermanentFailure = "permanent-failure";

    public static readonly IReadOnlyList<string> All =
    [
        HappyPath,
        FailThenRecover,
        PermanentFailure
    ];

    public static bool IsKnown(string scenarioName) =>
        All.Contains(scenarioName, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? scenarioName) =>
        All.FirstOrDefault(name => string.Equals(name, scenarioName, StringComparison.OrdinalIgnoreCase))
        ?? HappyPath;
}
