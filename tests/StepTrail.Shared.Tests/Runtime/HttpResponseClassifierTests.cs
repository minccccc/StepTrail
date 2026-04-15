using System.Net;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Workflows;
using StepTrail.Worker.Handlers;
using Xunit;

namespace StepTrail.Shared.Tests.Runtime;

public class HttpResponseClassifierTests
{
    private readonly HttpResponseClassifier _classifier = new();

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public void ClassifyResponse_Default2xx_IsSuccess(HttpStatusCode statusCode)
    {
        var result = _classifier.ClassifyResponse(statusCode);

        Assert.True(result.IsSuccess);
        Assert.Null(result.FailureClassification);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData((HttpStatusCode)429)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void ClassifyResponse_DefaultRetryableCodes_ReturnTransientFailure(HttpStatusCode statusCode)
    {
        var result = _classifier.ClassifyResponse(statusCode);

        Assert.False(result.IsSuccess);
        Assert.Equal(StepExecutionFailureClassification.TransientFailure, result.FailureClassification);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public void ClassifyResponse_DefaultNonRetryableClientErrors_ReturnPermanentFailure(HttpStatusCode statusCode)
    {
        var result = _classifier.ClassifyResponse(statusCode);

        Assert.False(result.IsSuccess);
        Assert.Equal(StepExecutionFailureClassification.PermanentFailure, result.FailureClassification);
    }

    [Fact]
    public void ClassifyResponse_CustomStatusCodesOverrideDefaults()
    {
        var configuration = new HttpResponseClassificationConfiguration(
            successStatusCodes: [204],
            retryableStatusCodes: [409]);

        var acceptedResult = _classifier.ClassifyResponse(HttpStatusCode.Accepted, configuration);
        var noContentResult = _classifier.ClassifyResponse(HttpStatusCode.NoContent, configuration);
        var conflictResult = _classifier.ClassifyResponse(HttpStatusCode.Conflict, configuration);

        Assert.False(acceptedResult.IsSuccess);
        Assert.Equal(StepExecutionFailureClassification.PermanentFailure, acceptedResult.FailureClassification);
        Assert.True(noContentResult.IsSuccess);
        Assert.Equal(StepExecutionFailureClassification.TransientFailure, conflictResult.FailureClassification);
    }

    [Fact]
    public void ClassifyTransportFailure_ReturnsTransientFailure()
    {
        var result = _classifier.ClassifyTransportFailure();

        Assert.False(result.IsSuccess);
        Assert.Equal(StepExecutionFailureClassification.TransientFailure, result.FailureClassification);
    }

    [Fact]
    public void ClassifyTimeout_ReturnsTransientFailure()
    {
        var result = _classifier.ClassifyTimeout();

        Assert.False(result.IsSuccess);
        Assert.Equal(StepExecutionFailureClassification.TransientFailure, result.FailureClassification);
    }
}
