namespace StepTrail.Shared.Definitions;

public sealed class TransformValueMapping
{
    private TransformValueMapping()
    {
        TargetPath = string.Empty;
        SourcePath = null;
        Operation = null;
    }

    public TransformValueMapping(string targetPath, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("Transform target path must not be empty.", nameof(targetPath));
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Transform source path must not be empty.", nameof(sourcePath));

        TargetPath = targetPath.Trim();
        SourcePath = sourcePath.Trim();
        Operation = null;
    }

    public TransformValueMapping(string targetPath, TransformValueOperation operation)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("Transform target path must not be empty.", nameof(targetPath));
        ArgumentNullException.ThrowIfNull(operation);

        TargetPath = targetPath.Trim();
        SourcePath = null;
        Operation = operation;
    }

    public string TargetPath { get; private set; }
    public string? SourcePath { get; private set; }
    public TransformValueOperation? Operation { get; private set; }

    public string NormalizedTargetPath => NormalizeTargetPath(TargetPath);

    public static string NormalizeTargetPath(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("Transform target path must not be empty.", nameof(targetPath));

        var normalized = targetPath.Trim();

        if (normalized == "$")
            throw new ArgumentException("Transform target path must not point at the root object.", nameof(targetPath));

        if (normalized.StartsWith("$.", StringComparison.Ordinal))
            normalized = normalized[2..];
        else if (normalized.StartsWith('$'))
            normalized = normalized[1..];

        normalized = normalized.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Transform target path must contain at least one field segment.", nameof(targetPath));

        var segments = normalized.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new ArgumentException("Transform target path must contain at least one field segment.", nameof(targetPath));

        if (segments.Any(segment => !IsValidPathSegment(segment)))
        {
            throw new ArgumentException(
                "Transform target path contains invalid characters. Only letters, digits, underscores, and hyphens are allowed in each segment.",
                nameof(targetPath));
        }

        return string.Join(".", segments);
    }

    private static bool IsValidPathSegment(string segment) =>
        segment.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');
}
