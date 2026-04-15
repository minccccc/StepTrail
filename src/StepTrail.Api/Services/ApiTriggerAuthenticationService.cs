using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace StepTrail.Api.Services;

/// <summary>
/// Validates shared-secret authentication for API trigger starts.
/// Authentication is evaluated before trigger resolution or workflow instance creation.
/// </summary>
public sealed class ApiTriggerAuthenticationService
{
    private readonly ApiTriggerAuthenticationOptions _options;

    public ApiTriggerAuthenticationService(IOptions<ApiTriggerAuthenticationOptions> options)
    {
        _options = options.Value;
    }

    public string HeaderName => string.IsNullOrWhiteSpace(_options.HeaderName)
        ? ApiTriggerAuthenticationOptions.DefaultHeaderName
        : _options.HeaderName.Trim();

    public void EnsureAuthenticated(string? presentedSecret)
    {
        var configuredSecret = _options.SharedSecret;

        if (string.IsNullOrWhiteSpace(configuredSecret))
        {
            if (_options.AllowUnauthenticated)
                return;

            throw new ApiTriggerAuthenticationConfigurationException(
                "API trigger authentication is not configured. Set ApiTriggerAuthentication:SharedSecret or explicitly enable ApiTriggerAuthentication:AllowUnauthenticated for local development.");
        }

        if (string.IsNullOrWhiteSpace(presentedSecret))
        {
            throw new ApiTriggerAuthenticationException(
                $"Missing API trigger credential. Supply the shared secret in the '{HeaderName}' header.");
        }

        if (!SecretsMatch(configuredSecret, presentedSecret))
        {
            throw new ApiTriggerAuthenticationException("Invalid API trigger credential.");
        }
    }

    private static bool SecretsMatch(string expected, string provided)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);

        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}

public sealed class ApiTriggerAuthenticationException : Exception
{
    public ApiTriggerAuthenticationException(string message) : base(message) { }
}

public sealed class ApiTriggerAuthenticationConfigurationException : Exception
{
    public ApiTriggerAuthenticationConfigurationException(string message) : base(message) { }
}
