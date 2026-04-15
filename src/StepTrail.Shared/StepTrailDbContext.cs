using Microsoft.EntityFrameworkCore;
using StepTrail.Shared.Definitions.Persistence;
using StepTrail.Shared.Entities;
using StepTrail.Shared.EntityConfigurations;

namespace StepTrail.Shared;

public class StepTrailDbContext : DbContext
{
    public StepTrailDbContext(DbContextOptions<StepTrailDbContext> options) : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowDefinitionStep> WorkflowDefinitionSteps => Set<WorkflowDefinitionStep>();
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<WorkflowStepExecution> WorkflowStepExecutions => Set<WorkflowStepExecution>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<WorkflowEvent> WorkflowEvents => Set<WorkflowEvent>();
    public DbSet<RecurringWorkflowSchedule> RecurringWorkflowSchedules => Set<RecurringWorkflowSchedule>();
    public DbSet<WorkflowSecret> WorkflowSecrets => Set<WorkflowSecret>();
    public DbSet<ExecutableWorkflowDefinitionRecord> ExecutableWorkflowDefinitions => Set<ExecutableWorkflowDefinitionRecord>();
    public DbSet<ExecutableTriggerDefinitionRecord> ExecutableTriggerDefinitions => Set<ExecutableTriggerDefinitionRecord>();
    public DbSet<ExecutableStepDefinitionRecord> ExecutableStepDefinitions => Set<ExecutableStepDefinitionRecord>();
    public DbSet<AlertRecord> AlertRecords => Set<AlertRecord>();
    public DbSet<AlertDeliveryRecord> AlertDeliveryRecords => Set<AlertDeliveryRecord>();
    public DbSet<PilotTelemetryEvent> PilotTelemetryEvents => Set<PilotTelemetryEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowDefinitionStepConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowInstanceConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowStepExecutionConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowEventConfiguration());
        modelBuilder.ApplyConfiguration(new RecurringWorkflowScheduleConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowSecretConfiguration());
        modelBuilder.ApplyConfiguration(new ExecutableWorkflowDefinitionRecordConfiguration());
        modelBuilder.ApplyConfiguration(new ExecutableTriggerDefinitionRecordConfiguration());
        modelBuilder.ApplyConfiguration(new ExecutableStepDefinitionRecordConfiguration());
        modelBuilder.ApplyConfiguration(new AlertRecordConfiguration());
        modelBuilder.ApplyConfiguration(new AlertDeliveryRecordConfiguration());
        modelBuilder.ApplyConfiguration(new PilotTelemetryEventConfiguration());
    }
}
