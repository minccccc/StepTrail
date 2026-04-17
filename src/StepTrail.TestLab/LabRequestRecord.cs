namespace StepTrail.TestLab;

public sealed record LabRequestRecord(
    DateTimeOffset ReceivedAtUtc,
    string Scenario,
    string Endpoint,
    string Method,
    int ResponseStatusCode,
    string Body);
