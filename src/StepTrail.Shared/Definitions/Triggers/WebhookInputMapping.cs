namespace StepTrail.Shared.Definitions;

public sealed class WebhookInputMapping
{
    private WebhookInputMapping()
    {
        TargetPath = string.Empty;
        SourcePath = string.Empty;
    }

    public WebhookInputMapping(string targetPath, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("Webhook input mapping target path must not be empty.", nameof(targetPath));
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Webhook input mapping source path must not be empty.", nameof(sourcePath));

        TargetPath = targetPath.Trim();
        SourcePath = sourcePath.Trim();
    }

    public string TargetPath { get; private set; }
    public string SourcePath { get; private set; }
}
