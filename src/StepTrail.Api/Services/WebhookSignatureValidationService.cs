using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;

namespace StepTrail.Api.Services;

/// <summary>
/// Validates optional webhook request signatures against the configured header,
/// secret reference, and supported algorithm set before any workflow instance is created.
/// </summary>
public sealed class WebhookSignatureValidationService
{
    private readonly StepTrailDbContext _db;

    public WebhookSignatureValidationService(StepTrailDbContext db)
    {
        _db = db;
    }

    public async Task ValidateAsync(
        WebhookSignatureValidationConfiguration configuration,
        string rawBody,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(rawBody);

        var actualSignature = headers?
            .FirstOrDefault(header => string.Equals(header.Key, configuration.HeaderName, StringComparison.OrdinalIgnoreCase))
            .Value;

        if (string.IsNullOrWhiteSpace(actualSignature))
        {
            throw new WebhookTriggerSignatureValidationException(
                $"Missing webhook signature header '{configuration.HeaderName}'.");
        }

        var secretValue = await _db.WorkflowSecrets
            .AsNoTracking()
            .Where(secret => secret.Name == configuration.SecretName)
            .Select(secret => secret.Value)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(secretValue))
        {
            throw new WebhookTriggerSignatureConfigurationException(
                $"Webhook signature validation secret '{configuration.SecretName}' was not found.");
        }

        var expectedSignature = BuildExpectedSignature(configuration, secretValue, rawBody);

        if (!SecureEquals(actualSignature.Trim(), expectedSignature))
        {
            throw new WebhookTriggerSignatureValidationException(
                $"Invalid webhook signature in header '{configuration.HeaderName}'.");
        }
    }

    private static string BuildExpectedSignature(
        WebhookSignatureValidationConfiguration configuration,
        string secretValue,
        string rawBody)
    {
        var secretBytes = Encoding.UTF8.GetBytes(secretValue);
        var bodyBytes = Encoding.UTF8.GetBytes(rawBody);

        var hashBytes = configuration.Algorithm switch
        {
            WebhookSignatureAlgorithm.HmacSha256 => HMACSHA256.HashData(secretBytes, bodyBytes),
            _ => throw new WebhookTriggerSignatureConfigurationException(
                $"Webhook signature algorithm '{configuration.Algorithm}' is not supported.")
        };

        var signatureValue = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return string.IsNullOrEmpty(configuration.SignaturePrefix)
            ? signatureValue
            : configuration.SignaturePrefix + signatureValue;
    }

    private static bool SecureEquals(string actual, string expected)
    {
        var normalizedActualBytes = Encoding.UTF8.GetBytes(actual.ToLowerInvariant());
        var normalizedExpectedBytes = Encoding.UTF8.GetBytes(expected.ToLowerInvariant());

        return normalizedActualBytes.Length == normalizedExpectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(normalizedActualBytes, normalizedExpectedBytes);
    }
}

public sealed class WebhookTriggerSignatureValidationException : Exception
{
    public WebhookTriggerSignatureValidationException(string message) : base(message) { }
}

public sealed class WebhookTriggerSignatureConfigurationException : Exception
{
    public WebhookTriggerSignatureConfigurationException(string message) : base(message) { }
}
