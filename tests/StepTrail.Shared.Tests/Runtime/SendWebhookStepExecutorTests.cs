using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Runtime;
using StepTrail.Shared.Workflows;
using StepTrail.Worker.Handlers;
using Xunit;

namespace StepTrail.Shared.Tests.Runtime;

public class SendWebhookStepExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_SuccessfulDelivery_ResolvesTemplatesAndReturnsDeliveryOutput()
    {
        HttpRequestMessage? capturedRequest = null;

        var executor = CreateExecutor(async request =>
        {
            capturedRequest = await CloneRequestAsync(request);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"accepted":true}""", Encoding.UTF8, "application/json")
            };
        });

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "notify-partner",
                StepType = "SendWebhook",
                StepConfiguration =
                    """
                    {
                      "webhookUrl": "https://hooks.example.com/customers/{{input.customerId}}",
                      "headers": {
                        "X-Source": "{{steps.fetch-order.output.source}}"
                      },
                      "body": "{\"customerId\":\"{{input.customerId}}\"}",
                      "timeoutSeconds": 5
                    }
                    """,
                State = StateWithInputAndStep(
                    """{"customerId":"cus_123"}""",
                    "fetch-order",
                    """{"source":"webhook"}""")
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("https://hooks.example.com/customers/cus_123", capturedRequest.RequestUri!.ToString());
        Assert.Equal("webhook", string.Join(", ", capturedRequest.Headers.GetValues("X-Source")));
        Assert.Equal("""{"customerId":"cus_123"}""", await capturedRequest.Content!.ReadAsStringAsync());

        using var outputDocument = JsonDocument.Parse(result.Output!);
        Assert.True(outputDocument.RootElement.GetProperty("delivered").GetBoolean());
        Assert.Equal(200, outputDocument.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal("https://hooks.example.com/customers/cus_123", outputDocument.RootElement.GetProperty("destination").GetString());
        Assert.True(outputDocument.RootElement.TryGetProperty("attemptedAtUtc", out _));
        Assert.Equal("""{"accepted":true}""", outputDocument.RootElement.GetProperty("responseBodyText").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_HttpFailure_ReturnsClassifiedFailureWithDeliveryOutput()
    {
        var executor = CreateExecutor(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("gateway error", Encoding.UTF8, "text/plain")
            }));

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "notify-partner",
                StepType = "SendWebhook",
                StepConfiguration = """{"webhookUrl":"https://hooks.example.com/events"}"""
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(StepExecutionFailureClassification.TransientFailure, result.Failure!.Classification);

        using var outputDocument = JsonDocument.Parse(result.Output!);
        Assert.False(outputDocument.RootElement.GetProperty("delivered").GetBoolean());
        Assert.Equal(502, outputDocument.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal("gateway error", outputDocument.RootElement.GetProperty("responseBodyText").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_TransportFailure_ReturnsTransientFailureWithUndeliveredOutput()
    {
        var executor = CreateExecutor(_ => throw new HttpRequestException("connection refused"));

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "notify-partner",
                StepType = "SendWebhook",
                StepConfiguration = """{"webhookUrl":"https://hooks.example.com/events"}"""
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(StepExecutionFailureClassification.TransientFailure, result.Failure!.Classification);

        using var outputDocument = JsonDocument.Parse(result.Output!);
        Assert.False(outputDocument.RootElement.GetProperty("delivered").GetBoolean());
        Assert.False(outputDocument.RootElement.TryGetProperty("statusCode", out _));
    }

    [Fact]
    public async Task ExecuteAsync_PlaceholderResolutionFailure_ReturnsInputResolutionFailure()
    {
        var executor = CreateExecutor(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "notify-partner",
                StepType = "SendWebhook",
                StepConfiguration = """{"webhookUrl":"https://hooks.example.com/customers/{{input.customerId}}"}""",
                State = StateWithInput("""{"other":"value"}""")
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(StepExecutionFailureClassification.InputResolutionFailure, result.Failure!.Classification);
        Assert.Contains("customerId", result.Failure.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutConfiguredBody_DoesNotForwardStepInput()
    {
        HttpRequestMessage? capturedRequest = null;

        var executor = CreateExecutor(async request =>
        {
            capturedRequest = await CloneRequestAsync(request);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "notify-partner",
                StepType = "SendWebhook",
                Input = """{"internal":"state"}""",
                StepConfiguration = """{"webhookUrl":"https://hooks.example.com/events"}"""
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedRequest);
        Assert.Equal("{}", await capturedRequest!.Content!.ReadAsStringAsync());
    }

    private static SendWebhookStepExecutor CreateExecutor(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
    {
        var messageHandler = new DelegateHttpMessageHandler(responseFactory);
        var httpClient = new HttpClient(messageHandler)
        {
            BaseAddress = new Uri("https://hooks.example.com/")
        };

        return new SendWebhookStepExecutor(
            new StubHttpClientFactory(httpClient),
            new HttpResponseClassifier(),
            NullLogger<SendWebhookStepExecutor>.Instance);
    }

    private static WorkflowState StateWithInput(string inputJson) =>
        new(
            new WorkflowStateMetadata(
                Guid.NewGuid(),
                "test-workflow",
                1,
                WorkflowInstanceStatus.Running,
                DateTimeOffset.UtcNow,
                null),
            triggerData: null,
            input: inputJson,
            steps: new Dictionary<string, WorkflowStepState>());

    private static WorkflowState StateWithInputAndStep(string inputJson, string stepKey, string outputJson) =>
        new(
            new WorkflowStateMetadata(
                Guid.NewGuid(),
                "test-workflow",
                1,
                WorkflowInstanceStatus.Running,
                DateTimeOffset.UtcNow,
                null),
            triggerData: null,
            input: inputJson,
            steps: new Dictionary<string, WorkflowStepState>
            {
                [stepKey] = new WorkflowStepState(
                    stepKey,
                    WorkflowStepExecutionStatus.Completed,
                    outputJson,
                    error: null,
                    attempts: [])
            });

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync();
            clone.Content = new StringContent(body, Encoding.UTF8);

            foreach (var header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _httpClient;

        public StubHttpClientFactory(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public HttpClient CreateClient(string name) => _httpClient;
    }

    private sealed class DelegateHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responseFactory;

        public DelegateHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _responseFactory(request);
    }
}
