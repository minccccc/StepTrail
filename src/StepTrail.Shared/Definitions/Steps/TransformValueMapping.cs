namespace StepTrail.Shared.Definitions;

public sealed class TransformValueMapping
{
    private TransformValueMapping()
    {
        TargetPath = string.Empty;
        SourcePath = string.Empty;
    }

    public TransformValueMapping(string targetPath, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("Transform target path must not be empty.", nameof(targetPath));
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Transform source path must not be empty.", nameof(sourcePath));

        TargetPath = targetPath.Trim();
        SourcePath = sourcePath.Trim();
    }

    public string TargetPath { get; private set; }
    public string SourcePath { get; private set; }
}
