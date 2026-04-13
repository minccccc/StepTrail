namespace StepTrail.Worker.Handlers;

/// <summary>
/// Thrown by HttpActivityHandler when the remote server returns a non-2xx status code.
/// Carries the serialized response (status code + body) so the worker can persist it
/// as the step execution's output even though the step is being failed.
/// </summary>
public sealed class HttpActivityException : Exception
{
    /// <summary>
    /// JSON-serialized response object: { "statusCode": N, "body": "..." }.
    /// Always set — callers can save this as the step's output for debugging.
    /// </summary>
    public string ResponseOutput { get; }

    public HttpActivityException(string message, string responseOutput)
        : base(message)
    {
        ResponseOutput = responseOutput;
    }
}
