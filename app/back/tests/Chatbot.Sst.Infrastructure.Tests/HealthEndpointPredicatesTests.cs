using Chatbot.Sst.Api;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Chatbot.Sst.Infrastructure.Tests;

public class HealthEndpointPredicatesTests
{
    [Fact]
    public void IncludeForLiveness_excludes_llm_dependency_checks()
    {
        var registration = NewRegistration("llm");

        Assert.False(HealthEndpointPredicates.IncludeForLiveness(registration));
    }

    [Fact]
    public void IncludeForLiveness_keeps_non_llm_checks()
    {
        var registration = NewRegistration("dispatch");

        Assert.True(HealthEndpointPredicates.IncludeForLiveness(registration));
    }

    [Fact]
    public void IncludeLlmOnly_matches_llm_dependency_checks()
    {
        var registration = NewRegistration("llm");

        Assert.True(HealthEndpointPredicates.IncludeLlmOnly(registration));
    }

    [Fact]
    public void IncludeLlmOnly_rejects_non_llm_checks()
    {
        var registration = NewRegistration("dispatch");

        Assert.False(HealthEndpointPredicates.IncludeLlmOnly(registration));
    }

    private static HealthCheckRegistration NewRegistration(params string[] tags)
        => new(
            "test",
            _ => throw new NotSupportedException("Factory is not used in predicate tests."),
            failureStatus: null,
            tags,
            timeout: default);
}
