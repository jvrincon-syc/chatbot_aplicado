using System.ComponentModel.DataAnnotations;

namespace Chatbot.Sst.Infrastructure.Dispatch;

/// <summary>Trusted configuration for the external chatbot backend that performs retrieval.</summary>
public sealed class ChatbotDispatchOptions
{
    public const string SectionName = "ChatbotDispatch";

    [Required]
    [Url]
    public string BaseUrl { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string BearerToken { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ProjectId { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string RagVariantId { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string SubmitPath { get; init; } = "/api/chatbot/questions";

    [Required(AllowEmptyStrings = false)]
    public string ReleasesPathTemplate { get; init; } = "/api/platform/projects/{project_id}/releases?page=1&page_size=100";

    [Required(AllowEmptyStrings = false)]
    public string RagReleasesPathTemplate { get; init; } = "/api/chatbot/rag-releases?project_id={project_id}&rag_variant_id={rag_variant_id}";

    [Range(1, 25)]
    public int DefaultTopK { get; init; } = 6;

    [Range(1, 600)]
    public int RequestTimeoutSeconds { get; init; } = 120;
}
