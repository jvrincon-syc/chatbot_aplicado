using System.ComponentModel.DataAnnotations;

namespace Chatbot.Sst.Infrastructure.Rag;

/// <summary>
/// Trusted server-side configuration binding the deployment to one RAG product.
/// All three identifiers are required — the app fails closed if any is missing.
/// </summary>
public sealed class RagTargetOptions
{
    public const string SectionName = "RagTarget";

    [Required(AllowEmptyStrings = false)]
    public string ProjectId { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string RagVariantId { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string RagReleaseId { get; init; } = string.Empty;
}
