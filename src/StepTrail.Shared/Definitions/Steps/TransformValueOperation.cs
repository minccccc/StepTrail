namespace StepTrail.Shared.Definitions;

public sealed class TransformValueOperation
{
    private readonly List<string> _parts = [];
    private readonly List<string> _arguments = [];

    private TransformValueOperation()
    {
        SourcePath = null;
        DefaultValue = null;
        Template = null;
    }

    private TransformValueOperation(
        TransformOperationType type,
        string? sourcePath,
        string? defaultValue,
        string? template,
        IEnumerable<string>? parts,
        IEnumerable<string>? arguments)
    {
        Type = type;
        SourcePath = string.IsNullOrWhiteSpace(sourcePath) ? null : sourcePath.Trim();
        DefaultValue = defaultValue;
        Template = template;

        if (parts is not null)
            _parts.AddRange(parts);

        if (arguments is not null)
            _arguments.AddRange(arguments);

        Validate(type, SourcePath, DefaultValue, Template, _parts, _arguments);
    }

    public TransformOperationType Type { get; private set; }
    public string? SourcePath { get; private set; }
    public string? DefaultValue { get; private set; }
    public string? Template { get; private set; }
    public IReadOnlyList<string> Parts => _parts;
    public IReadOnlyList<string> Arguments => _arguments;

    public static TransformValueOperation CreateDefaultValue(string sourcePath, string defaultValue)
    {
        ArgumentNullException.ThrowIfNull(defaultValue);

        return new TransformValueOperation(
            TransformOperationType.DefaultValue,
            sourcePath,
            defaultValue,
            template: null,
            parts: null,
            arguments: null);
    }

    public static TransformValueOperation CreateConcatenate(IEnumerable<string> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        return new TransformValueOperation(
            TransformOperationType.Concatenate,
            sourcePath: null,
            defaultValue: null,
            template: null,
            parts: parts.ToList(),
            arguments: null);
    }

    public static TransformValueOperation CreateFormatString(string template, IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return new TransformValueOperation(
            TransformOperationType.FormatString,
            sourcePath: null,
            defaultValue: null,
            template,
            parts: null,
            arguments: arguments.ToList());
    }

    private static void Validate(
        TransformOperationType type,
        string? sourcePath,
        string? defaultValue,
        string? template,
        IReadOnlyList<string> parts,
        IReadOnlyList<string> arguments)
    {
        switch (type)
        {
            case TransformOperationType.DefaultValue:
                if (string.IsNullOrWhiteSpace(sourcePath))
                    throw new ArgumentException("Default value transform operations require a source path.", nameof(sourcePath));
                if (defaultValue is null)
                    throw new ArgumentNullException(nameof(defaultValue), "Default value transform operations require a default value.");
                break;

            case TransformOperationType.Concatenate:
                if (parts.Count == 0)
                    throw new ArgumentException("Concatenate transform operations require at least one part.", nameof(parts));
                if (parts.Any(part => part is null))
                    throw new ArgumentException("Concatenate transform operation parts must not be null.", nameof(parts));
                break;

            case TransformOperationType.FormatString:
                if (string.IsNullOrWhiteSpace(template))
                    throw new ArgumentException("Format string transform operations require a template.", nameof(template));
                if (arguments.Count == 0)
                    throw new ArgumentException("Format string transform operations require at least one argument.", nameof(arguments));
                if (arguments.Any(argument => argument is null))
                    throw new ArgumentException("Format string transform operation arguments must not be null.", nameof(arguments));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Transform operation type is not supported.");
        }
    }
}
