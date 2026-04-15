namespace StepTrail.Shared.Runtime;

public sealed class WorkflowStartNotFoundException : Exception
{
    public WorkflowStartNotFoundException(string message) : base(message)
    {
    }
}

public sealed class WorkflowStartDefinitionNotActiveException : Exception
{
    public WorkflowStartDefinitionNotActiveException(string message) : base(message)
    {
    }
}

public sealed class WorkflowStartTenantNotFoundException : Exception
{
    public WorkflowStartTenantNotFoundException(string message) : base(message)
    {
    }
}
