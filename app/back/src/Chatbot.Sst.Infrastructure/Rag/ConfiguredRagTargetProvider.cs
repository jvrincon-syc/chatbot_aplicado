using Chatbot.Sst.Application.Abstractions;
using Chatbot.Sst.Domain;
using Microsoft.Extensions.Options;

namespace Chatbot.Sst.Infrastructure.Rag;

/// <summary>Resolves the RagTarget from validated configuration. Never trusts client input.</summary>
public sealed class ConfiguredRagTargetProvider : IRagTargetProvider
{
    private readonly RagTarget _target;

    public ConfiguredRagTargetProvider(IOptions<RagTargetOptions> options)
    {
        var o = options.Value; // DataAnnotations validation (ValidateOnStart) already enforced non-empty.
        _target = new RagTarget(o.ProjectId, o.RagVariantId, o.RagReleaseId);
    }

    public RagTarget GetTarget() => _target;
}
