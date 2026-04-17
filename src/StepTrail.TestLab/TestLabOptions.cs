namespace StepTrail.TestLab;

public sealed class TestLabOptions
{
    public string StepTrailApiBaseUrl { get; set; } = "http://localhost:5000";
    public string PublicBaseUrl { get; set; } = "http://localhost:5150";
    public bool AutoSetupOnStartup { get; set; } = true;
}
