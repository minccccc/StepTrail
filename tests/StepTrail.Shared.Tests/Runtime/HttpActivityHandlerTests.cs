using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Runtime;
using StepTrail.Shared.Workflows;
using StepTrail.Worker.Handlers;
using Xunit;

namespace StepTrail.Shared.Tests.Runtime;

public class HttpActivityHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_SuccessfulRequest_ResolvesTemplatesAndReturnsNormalizedOutput()
    {
        HttpRequestMessage? capturedRequest = null;

        var handler = CreateHandler(async request =>
        {
            capturedRequest = await CloneRequestAsync(request);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"accepted":true}""", Encoding.UTF8, "application/json")
            };
            response.Headers.Add("x-request-id", "req_123");
            return response;
        });

        var result = await handler.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "call-api",
                StepType = "HttpRequest",
                Input = """{"fallback":"unused"}""",
                StepConfiguration =
                    """
                    {
                      "url": "https://api.example.com/customers/{{input.customerId}}/orders/{{steps.fetch-order.output.orderId}}",
                      "method": "POST",
                      "headers": {
                        "Authorization": "Bearer {{secrets.api-key}}",
                        "X-Source": "{{steps.fetch-order.output.source}}"
                      },
                      "body": "{\"customerId\":\"{{input.customerId}}\"}"
                    }
                    """,
                State = StateWithInputAndStep(
                    """{"customerId":"cus_123"}""",
                    "fetch-order",
                    """{"orderId":"ord_789","source":"webhook"}"""),
                Secrets = new Dictionary<string, string>
                {
                    ["api-key"] = "secret_123"
                }
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal(
            "https://api.example.com/customers/cus_123/orders/ord_789",
            capturedRequest.RequestUri!.ToString());
        Assert.Equal("Bearer secret_123", string.Join(", ", capturedRequest.Headers.GetValues("Authorization")));
        Assert.Equal("webhook", string.Join(", ", capturedRequest.Headers.GetValues("X-Source")));
        Assert.Equal("""{"customerId":"cus_123"}""", await capturedRequest.Content!.ReadAsStringAsync());

        using var outputDocument = JsonDocument.Parse(result.Output!);
        Assert.Equal(200, outputDocument.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(JsonValueKind.Object, outputDocument.RootElement.GetProperty("body").ValueKind);
        Assert.True(outputDocument.RootElement.GetProperty("body").GetProperty("accepted").GetBoolean());
        Assert.Equal("""{"accepted":true}""", outputDocument.RootElement.GetProperty("bodyText").GetString());
        Assert.Equal("application/json", outputDocument.RootElement.GetProperty("contentType").GetString());
        Assert.Equal(
            "req_123",
            outputDocument.RootElement.GetProperty("headers").GetProperty("x-request-id").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_RequestTimeout_ReturnsTransientFailure()
    {
        var handler = CreateHandler(async request =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), request.GetCancellationToken());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await handler.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "call-api",
                StepType = "HttpRequest",
                StepConfiguration =
                    """
                    {
                      "url": "https://api.example.com/orders",
                      "method": "GET",
                      "timeoutSeconds": 1
                    }
                    """
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(StepExecutionFailureClassification.TransientFailure, result.Failure!.Classification);
        Assert.Contains("timed out", result.Failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Output);

        using var outputDocument = JsonDocument.Parse(result.Output!);
        Assert.Equal(0, outputDocument.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal("GET", outputDocument.RootElement.GetProperty("requestMethod").GetString());
        Assert.Equal("https://api.example.com/orders", outputDocument.RootElement.GetProperty("requestUrl").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ServerErrorResponse_ReturnsTransientFailureWithNormalizedOutput()
    {
        var handler = CreateHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("gateway error", Encoding.UTF8, "text/plain")
            };
            response.Headers.Add("x-downstream-id", "downstream_123");
            return Task.FromResult(response);
        });

        var result = await handler.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "call-api",
                StepType = "HttpRequest",
                StepConfiguration =
                    """
                    {
                      "url": "https://api.example.com/orders",
                      "method": "GET"
                    }
                    """
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(StepExecutionFailureClassification.TransientFailure, result.Failure!.Classification);
        Assert.Contains("502", result.Failure.Message);
        Assert.NotNull(result.Output);

        using var outputDocument = JsonDocument.Parse(result.Output!);
        Assert.Equal(502, outputDocument.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(JsonValueKind.String, outputDocument.RootElement.GetProperty("body").ValueKind);
        Assert.Equal("gateway error", outputDocument.RootElement.GetProperty("body").GetString());
        Assert.Equal("gateway error", outputDocument.RootElement.GetProperty("bodyText").GetString());
        Assert.Equal("text/plain", outputDocument.RootElement.GetProperty("contentType").GetString());
        Assert.Equal(
            "downstream_123",
            outputDocument.RootElement.GetProperty("headers").GetProperty("x-downstream-id").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ClientErrorResponse_ReturnsPermanentFailure()
    {
        var handler = CreateHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("missing", Encoding.UTF8, "text/plain")
            }));

        var result = await handler.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "call-api",
                StepType = "HttpRequest",
                StepConfiguration =
                    """
                    {
                      "url": "https://api.example.com/orders",
                      "method": "GET"
                    }
                    """
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(StepExecutionFailureClassification.PermanentFailure, result.Failure!.Classification);
        Assert.Contains("404", result.Failure.Message);
    }

    [Fact]
    public async Task ExecuteAsync_CustomRetryableStatusCodeOverride_ReturnsTransientFailure()
    {
        var handler = CreateHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("conflict", Encoding.UTF8, "text/plain")
            }));

        var result = await handler.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "call-api",
                StepType = "HttpRequest",
                StepConfiguration =
                    """
                    {
                      "url": "https://api.example.com/orders",
                      "method": "POST",
                      "responseClassification": {
                        "retryableStatusCodes": [409]
                      }
                    }
                    """
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(StepExecutionFailureClassification.TransientFailure, result.Failure!.Classification);
        Assert.Contains("409", result.Failure.Message);
    }

    [Fact]
    public async Task ExecuteAsync_TransportFailure_ReturnsTransientFailureWithRequestDebugOutput()
    {
        var handler = CreateHandler(_ => throw new HttpRequestException("connection refused"));

        var result = await handler.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "call-api",
                StepType = "HttpRequest",
                StepConfiguration =
                    """
                    {
                      "url": "https://api.example.com/orders",
                      "method": "POST"
                    }
                    """
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(StepExecutionFailureClassification.TransientFailure, result.Failure!.Classification);
        Assert.NotNull(result.Output);

        using var outputDocument = JsonDocument.Parse(result.Output!);
        Assert.Equal(0, outputDocument.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(string.Empty, outputDocument.RootElement.GetProperty("bodyText").GetString());
        Assert.Equal("POST", outputDocument.RootElement.GetProperty("requestMethod").GetString());
        Assert.Equal("https://api.example.com/orders", outputDocument.RootElement.GetProperty("requestUrl").GetString());
    }

    private static HttpActivityHandler CreateHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
    {
        var messageHandler = new DelegateHttpMessageHandler(responseFactory);
        var httpClient = new HttpClient(messageHandler)
        {
            BaseAddress = new Uri("https://api.example.com/")
        };

        return new HttpActivityHandler(
            new StubHttpClientFactory(httpClient),
            new HttpResponseClassifier(),
            NullLogger<HttpActivityHandler>.Instance);
    }

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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Options.Set(new HttpRequestOptionsKey<CancellationToken>("StepTrailTestCancellationToken"), cancellationToken);
            return _responseFactory(request);
        }
    }
}

internal static class HttpRequestMessageTestExtensions
{
    public static CancellationToken GetCancellationToken(this HttpRequestMessage request) =>
        request.Options.TryGetValue(new HttpRequestOptionsKey<CancellationToken>("StepTrailTestCancellationToken"), out var token)
            ? token
            : CancellationToken.None;
}
