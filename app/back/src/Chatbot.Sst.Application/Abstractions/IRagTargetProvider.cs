using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Application.Abstractions;

/// <summary>
/// Resolves the deployment's RagTarget from trusted server-side configuration.
/// Fails closed: throws if any identifier is missing. The browser is never the authority.
/// </summary>
public interface IRagTargetProvider
{
    RagTarget GetTarget();
}
