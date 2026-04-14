namespace StepTrail.Shared.Definitions;

public sealed class TransformStepConfiguration
{
    private readonly List<TransformValueMapping> _mappings = [];

    private TransformStepConfiguration()
    {
    }

    public TransformStepConfiguration(IEnumerable<TransformValueMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);

        var normalizedMappings = mappings.ToList();

        if (normalizedMappings.Count == 0)
            throw new ArgumentException("Transform step configuration must contain at least one mapping.", nameof(mappings));

        _mappings.AddRange(normalizedMappings);
    }

    public IReadOnlyList<TransformValueMapping> Mappings => _mappings;
}
