using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StepTrail.Shared.Entities;
using StepTrail.Worker.Alerts;
using Xunit;

namespace StepTrail.Shared.Tests;

public class AlertDeliveryTests
{
    [Fact]
    public void AlertPayload_ContainsAllExpectedFields()
    {
        var payload = new AlertPayload
        {
            AlertType = "WorkflowFailed",
            WorkflowInstanceId = Guid.NewGuid(),
            WorkflowKey = "order-fulfillment",
            WorkflowVersion = 2,
            Status = "Failed",
            StepKey = "charge-payment",
            Attempt = 3,
            Message = "Workflow failed after 3 attempt(s) on step 'charge-payment'",
            Error = "Payment gateway timeout",
            OccurredAtUtc = DateTimeOffset.UtcNow
        };

        Assert.Equal("WorkflowFailed", payload.AlertType);
        Assert.Equal("order-fulfillment", payload.WorkflowKey);
        Assert.Equal(2, payload.WorkflowVersion);
        Assert.Equal("Failed", payload.Status);
        Assert.Equal("charge-payment", payload.StepKey);
        Assert.Equal(3, payload.Attempt);
        Assert.NotEmpty(payload.Message);
        Assert.Equal("Payment gateway timeout", payload.Error);
    }

    [Fact]
    public async Task ConsoleLogChannel_ReturnsSuccess()
    {
        var channel = new ConsoleLogAlertChannel(NullLogger<ConsoleLogAlertChannel>.Instance);

        var result = await channel.SendAsync(CreateTestPayload(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ConsoleLogChannel_HasExpectedChannelName()
    {
        var channel = new ConsoleLogAlertChannel(NullLogger<ConsoleLogAlertChannel>.Instance);

        Assert.Equal("ConsoleLog", channel.ChannelName);
    }

    [Fact]
    public void AlertDeliveryResult_Success_HasNoError()
    {
        var result = new AlertDeliveryResult(true);

        Assert.True(result.Success);
        Assert.Null(result.Error);
    }

    [Fact]
    public void AlertDeliveryResult_Failure_ContainsError()
    {
        var result = new AlertDeliveryResult(false, "Connection refused");

        Assert.False(result.Success);
        Assert.Equal("Connection refused", result.Error);
    }

    [Fact]
    public async Task AlertService_PersistsAlertRecord_WhenChannelSucceeds()
    {
        var successChannel = new FakeAlertChannel("TestChannel", new AlertDeliveryResult(true));
        using var db = TestDbContextFactory.Create();
        var service = new AlertService([successChannel], db, NullLogger<AlertService>.Instance);
        var payload = CreateTestPayload();

        await service.SendAsync(payload, CancellationToken.None);

        var alert = Assert.Single(db.AlertRecords.ToList());
        Assert.Equal(payload.AlertType, alert.AlertType);
        Assert.Equal(payload.WorkflowInstanceId, alert.WorkflowInstanceId);
        Assert.Equal(payload.WorkflowKey, alert.WorkflowKey);
        Assert.Equal(payload.StepKey, alert.StepKey);
        Assert.Equal(payload.Attempt, alert.Attempt);
        Assert.Equal(payload.Error, alert.Cause);

        var delivery = Assert.Single(db.AlertDeliveryRecords.ToList());
        Assert.Equal(alert.Id, delivery.AlertRecordId);
        Assert.Equal("TestChannel", delivery.Channel);
        Assert.Equal("Delivered", delivery.Status);
        Assert.Null(delivery.Error);
    }

    [Fact]
    public async Task AlertService_PersistsFailedDelivery_WhenChannelFails()
    {
        var failChannel = new FakeAlertChannel("FailChannel", new AlertDeliveryResult(false, "Connection refused"));
        using var db = TestDbContextFactory.Create();
        var service = new AlertService([failChannel], db, NullLogger<AlertService>.Instance);

        await service.SendAsync(CreateTestPayload(), CancellationToken.None);

        var delivery = Assert.Single(db.AlertDeliveryRecords.ToList());
        Assert.Equal("FailChannel", delivery.Channel);
        Assert.Equal("Failed", delivery.Status);
        Assert.Equal("Connection refused", delivery.Error);
    }

    [Fact]
    public async Task AlertService_PersistsFailedDelivery_WhenChannelThrows()
    {
        var throwChannel = new ThrowingAlertChannel("BrokenChannel");
        using var db = TestDbContextFactory.Create();
        var service = new AlertService([throwChannel], db, NullLogger<AlertService>.Instance);

        await service.SendAsync(CreateTestPayload(), CancellationToken.None);

        var delivery = Assert.Single(db.AlertDeliveryRecords.ToList());
        Assert.Equal("BrokenChannel", delivery.Channel);
        Assert.Equal("Failed", delivery.Status);
        Assert.Contains("Boom", delivery.Error);
    }

    [Fact]
    public async Task AlertService_PersistsMultipleDeliveries_ForMultipleChannels()
    {
        var channel1 = new FakeAlertChannel("Channel1", new AlertDeliveryResult(true));
        var channel2 = new FakeAlertChannel("Channel2", new AlertDeliveryResult(false, "Timeout"));
        using var db = TestDbContextFactory.Create();
        var service = new AlertService([channel1, channel2], db, NullLogger<AlertService>.Instance);

        await service.SendAsync(CreateTestPayload(), CancellationToken.None);

        var alert = Assert.Single(db.AlertRecords.ToList());
        var deliveries = db.AlertDeliveryRecords.Where(d => d.AlertRecordId == alert.Id).ToList();
        Assert.Equal(2, deliveries.Count);

        var d1 = deliveries.Single(d => d.Channel == "Channel1");
        Assert.Equal("Delivered", d1.Status);

        var d2 = deliveries.Single(d => d.Channel == "Channel2");
        Assert.Equal("Failed", d2.Status);
        Assert.Equal("Timeout", d2.Error);
    }

    private static AlertPayload CreateTestPayload() => new()
    {
        AlertType = AlertRuleType.WorkflowFailed.ToString(),
        WorkflowInstanceId = Guid.NewGuid(),
        WorkflowKey = "test-workflow",
        WorkflowVersion = 1,
        Status = "Failed",
        StepKey = "test-step",
        Attempt = 3,
        Message = "Workflow failed after 3 attempt(s) on step 'test-step'",
        Error = "Something went wrong",
        OccurredAtUtc = DateTimeOffset.UtcNow
    };

    private sealed class FakeAlertChannel(string name, AlertDeliveryResult result) : IAlertChannel
    {
        public string ChannelName => name;
        public Task<AlertDeliveryResult> SendAsync(AlertPayload payload, CancellationToken ct) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingAlertChannel(string name) : IAlertChannel
    {
        public string ChannelName => name;
        public Task<AlertDeliveryResult> SendAsync(AlertPayload payload, CancellationToken ct) =>
            throw new InvalidOperationException("Boom");
    }
}

/// <summary>
/// Creates an in-memory StepTrailDbContext for unit testing alert persistence.
/// </summary>
internal static class TestDbContextFactory
{
    public static StepTrail.Shared.StepTrailDbContext Create()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<StepTrail.Shared.StepTrailDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new StepTrail.Shared.StepTrailDbContext(options);
    }
}
