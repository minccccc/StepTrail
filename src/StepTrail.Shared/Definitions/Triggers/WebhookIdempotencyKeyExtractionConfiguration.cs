namespace StepTrail.Shared.Definitions;

public sealed class WebhookIdempotencyKeyExtractionConfiguration
{
    private WebhookIdempotencyKeyExtractionConfiguration()
    {
        SourcePath = string.Empty;
    }

    public WebhookIdempotencyKeyExtractionConfiguration(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Webhook idempotency key source path must not be empty.", nameof(sourcePath));

        SourcePath = sourcePath.Trim();
    }

    public string SourcePath { get; private set; }
}
