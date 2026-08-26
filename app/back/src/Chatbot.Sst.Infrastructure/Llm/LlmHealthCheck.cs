using Chatbot.Sst.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Chatbot.Sst.Infrastructure.Llm;

/// <summary>Health check that verifies the local LLM endpoint is reachable.</summary>
public sealed class LlmHealthCheck : IHealthCheck
{
    private readonly ILlmProvider _llm;

    public LlmHealthCheck(ILlmProvider llm) => _llm = llm;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var ok = await _llm.IsAvailableAsync(cancellationToken);
        return ok
            ? HealthCheckResult.Healthy("Local LLM endpoint reachable.")
            : HealthCheckResult.Unhealthy("Local LLM endpoint not reachable.");
    }
}
