using Xunit;

namespace StepTrail.Shared.Tests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresWorkflowDefinitionRepositoryCollection : ICollectionFixture<PostgresWorkflowDefinitionRepositoryFixture>
{
    public const string Name = "postgres-workflow-definition-repository";
}
