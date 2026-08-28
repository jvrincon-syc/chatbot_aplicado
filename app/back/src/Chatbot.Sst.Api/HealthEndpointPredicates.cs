using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Chatbot.Sst.Api;

public static class HealthEndpointPredicates
{
    public static bool IncludeForLiveness(HealthCheckRegistration registration)
        => !registration.Tags.Contains("llm", StringComparer.Ordinal);

    public static bool IncludeLlmOnly(HealthCheckRegistration registration)
        => registration.Tags.Contains("llm", StringComparer.Ordinal);
}
