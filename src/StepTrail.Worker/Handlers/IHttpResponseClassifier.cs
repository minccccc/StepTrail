using System.Net;
using StepTrail.Shared.Definitions;

namespace StepTrail.Worker.Handlers;

public interface IHttpResponseClassifier
{
    HttpResponseClassificationResult ClassifyResponse(
        HttpStatusCode statusCode,
        HttpResponseClassificationConfiguration? configuration = null);

    HttpResponseClassificationResult ClassifyTransportFailure();

    HttpResponseClassificationResult ClassifyTimeout();
}
