using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chatbot.Sst.Application.Abstractions;
using Chatbot.Sst.Domain;
using Microsoft.Extensions.Options;

namespace Chatbot.Sst.Infrastructure.Dispatch;

/// <summary>
/// Speaks the external chatbot backend contract:
/// 1. resolve the published release for the configured project + variant
/// 2. dispatch the user question with bearer auth
/// </summary>
public sealed class HttpChatbotDispatchClient : IChatbotDispatchClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // ponytail: TTL cache, not event-driven invalidation. A newly published release
    // takes up to 5 minutes to be picked up. Upgrade to invalidate-on-publish (e.g. via
    // a version/etag check) if that lag ever matters in practice.
    private static readonly TimeSpan ReleaseCacheTtl = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;
    private readonly ChatbotDispatchOptions _options;
    private readonly SemaphoreSlim _releaseCacheLock = new(1, 1);
    private PublishedRelease? _cachedRelease;
    private DateTimeOffset _cachedAtUtc;

    public HttpChatbotDispatchClient(HttpClient http, IOptions<ChatbotDispatchOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<ChatDispatchReceipt> DispatchAsync(ChatQuestionSubmission submission, CancellationToken cancellationToken)
    {
        var release = await ResolvePublishedReleaseAsync(cancellationToken);
        var topK = submission.TopK is >= 1 and <= 25 ? submission.TopK : _options.DefaultTopK;

        var dispatchRequest = new DispatchRequest(
            _options.ProjectId,
            _options.RagVariantId,
            release.RagReleaseId,
            submission.Question,
            submission.ConversationId,
            submission.MessageId,
            topK);
        var dispatchRequestJson = JsonSerializer.Serialize(dispatchRequest, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.SubmitPath)
        {
            Content = new StringContent(dispatchRequestJson, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.BearerToken);

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildExceptionAsync(
                response,
                fallbackCode: "CHATBOT_WEBHOOK_DELIVERY_FAILED",
                fallbackMessage: "El backend de contexto no aceptó la pregunta del chatbot.",
                cancellationToken);
        }

        var payload = await response.Content.ReadFromJsonAsync<DispatchResponse>(JsonOptions, cancellationToken)
                      ?? throw new ChatDispatchException(
                          "CHATBOT_WEBHOOK_DELIVERY_FAILED",
                          "El backend de contexto respondió sin body al aceptar la pregunta.",
                          (int)response.StatusCode);

        return new ChatDispatchReceipt(
            payload.DispatchId,
            payload.ProjectId,
            payload.RagVariantId,
            payload.RagReleaseId,
            payload.RetrievalProfileId,
            payload.Question,
            payload.MessageId ?? submission.MessageId ?? string.Empty,
            payload.ChunksSent,
            payload.WebhookStatusCode,
            payload.DispatchedAt,
            payload.ConversationId);
    }

    public async Task<IReadOnlyList<RagReleaseSummary>> ListRagReleasesAsync(CancellationToken cancellationToken)
    {
        var encodedProjectId = Uri.EscapeDataString(_options.ProjectId);
        var encodedRagVariantId = Uri.EscapeDataString(_options.RagVariantId);
        var path = _options.RagReleasesPathTemplate
            .Replace("{project_id}", encodedProjectId, StringComparison.Ordinal)
            .Replace("{rag_variant_id}", encodedRagVariantId, StringComparison.Ordinal);

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.BearerToken);

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildExceptionAsync(
                response,
                fallbackCode: "CHATBOT_RAG_RELEASES_LOOKUP_FAILED",
                fallbackMessage: "No se pudieron consultar las releases RAG disponibles.",
                cancellationToken);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return EnumerateRagReleases(document.RootElement)
            .OrderByDescending(release => release.ReleaseNumber)
            .ToArray();
    }

    private async Task<PublishedRelease> ResolvePublishedReleaseAsync(CancellationToken cancellationToken)
    {
        if (_cachedRelease is { } cached && DateTimeOffset.UtcNow - _cachedAtUtc < ReleaseCacheTtl)
        {
            return cached;
        }

        await _releaseCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedRelease is { } stillCached && DateTimeOffset.UtcNow - _cachedAtUtc < ReleaseCacheTtl)
            {
                return stillCached;
            }

            var resolved = await FetchPublishedReleaseAsync(cancellationToken);
            _cachedRelease = resolved;
            _cachedAtUtc = DateTimeOffset.UtcNow;
            return resolved;
        }
        finally
        {
            _releaseCacheLock.Release();
        }
    }

    private async Task<PublishedRelease> FetchPublishedReleaseAsync(CancellationToken cancellationToken)
    {
        var encodedProjectId = Uri.EscapeDataString(_options.ProjectId);
        var path = _options.ReleasesPathTemplate
            .Replace("{project_id}", encodedProjectId, StringComparison.Ordinal)
            .Replace("{projectId}", encodedProjectId, StringComparison.Ordinal);

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.BearerToken);

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildExceptionAsync(
                response,
                fallbackCode: "CHATBOT_RELEASE_LOOKUP_FAILED",
                fallbackMessage: "No se pudo consultar la release publicada para el chatbot.",
                cancellationToken);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var match = EnumerateReleases(document.RootElement)
            .Where(release =>
                string.Equals(release.ProjectId, _options.ProjectId, StringComparison.Ordinal) &&
                string.Equals(release.RagVariantId, _options.RagVariantId, StringComparison.Ordinal) &&
                string.Equals(release.State, "published", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(release => release.PublishedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        return match
               ?? throw new ChatDispatchException(
                   "CHATBOT_RELEASE_NOT_PUBLISHED",
                   "No existe una release published para el proyecto y la variante configurados.");
    }

    private static IEnumerable<PublishedRelease> EnumerateReleases(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (TryParseRelease(item, out var release))
                {
                    yield return release;
                }
            }

            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in property.Value.EnumerateArray())
            {
                if (TryParseRelease(item, out var release))
                {
                    yield return release;
                }
            }
        }
    }

    private static bool TryParseRelease(JsonElement item, out PublishedRelease release)
    {
        release = default!;
        if (item.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var projectId = GetString(item, "project_id");
        var ragVariantId = GetString(item, "rag_variant_id");
        var ragReleaseId = GetString(item, "rag_release_id");
        var state = GetString(item, "state");
        if (string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(ragVariantId) ||
            string.IsNullOrWhiteSpace(ragReleaseId) ||
            string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        release = new PublishedRelease(
            projectId,
            ragVariantId,
            ragReleaseId,
            state,
            GetDateTimeOffset(item, "published_at"));
        return true;
    }

    private static IEnumerable<RagReleaseSummary> EnumerateRagReleases(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (TryParseRagRelease(item, out var release))
                {
                    yield return release;
                }
            }

            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in property.Value.EnumerateArray())
            {
                if (TryParseRagRelease(item, out var release))
                {
                    yield return release;
                }
            }
        }
    }

    private static bool TryParseRagRelease(JsonElement item, out RagReleaseSummary release)
    {
        release = default!;
        if (item.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var ragReleaseId = GetString(item, "rag_release_id");
        var projectId = GetString(item, "project_id");
        var ragVariantId = GetString(item, "rag_variant_id");
        var state = GetString(item, "state");
        var createdAt = GetDateTimeOffset(item, "created_at");
        if (string.IsNullOrWhiteSpace(ragReleaseId) ||
            string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(ragVariantId) ||
            string.IsNullOrWhiteSpace(state) ||
            createdAt is null)
        {
            return false;
        }

        release = new RagReleaseSummary(
            ragReleaseId,
            projectId,
            ragVariantId,
            state,
            GetInt32(item, "release_number") ?? 0,
            createdAt.Value,
            GetDateTimeOffset(item, "validated_at"));
        return true;
    }

    private static int? GetInt32(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static async Task<ChatDispatchException> BuildExceptionAsync(
        HttpResponseMessage response,
        string fallbackCode,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;
        var fallbackAuthCode = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "HTTP_AUTH_REQUIRED",
            HttpStatusCode.Forbidden => "HTTP_AUTH_INVALID_CREDENTIALS",
            _ => fallbackCode
        };

        string? raw;
        try
        {
            raw = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            raw = null;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ChatDispatchException(fallbackAuthCode, fallbackMessage, statusCode);
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var code = GetString(root, "error_code")
                       ?? GetString(root, "code")
                       ?? GetString(root, "error");
            var message = GetString(root, "message")
                          ?? GetString(root, "detail")
                          ?? fallbackMessage;
            return new ChatDispatchException(code ?? fallbackAuthCode, message, statusCode);
        }
        catch (JsonException)
        {
            return new ChatDispatchException(fallbackAuthCode, fallbackMessage, statusCode);
        }
    }

    private static string? GetString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? GetDateTimeOffset(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    private sealed record DispatchRequest(
        [property: JsonPropertyName("project_id")] string ProjectId,
        [property: JsonPropertyName("rag_variant_id")] string RagVariantId,
        [property: JsonPropertyName("rag_release_id")] string RagReleaseId,
        [property: JsonPropertyName("question")] string Question,
        [property: JsonPropertyName("conversation_id")] string? ConversationId,
        [property: JsonPropertyName("message_id")] string? MessageId,
        [property: JsonPropertyName("top_k")] int TopK);

    private sealed record DispatchResponse(
        [property: JsonPropertyName("dispatch_id")] string DispatchId,
        [property: JsonPropertyName("project_id")] string ProjectId,
        [property: JsonPropertyName("rag_variant_id")] string RagVariantId,
        [property: JsonPropertyName("rag_release_id")] string RagReleaseId,
        [property: JsonPropertyName("retrieval_profile_id")] string RetrievalProfileId,
        [property: JsonPropertyName("question")] string Question,
        [property: JsonPropertyName("conversation_id")] string? ConversationId,
        [property: JsonPropertyName("message_id")] string? MessageId,
        [property: JsonPropertyName("chunks_sent")] int ChunksSent,
        [property: JsonPropertyName("webhook_status_code")] int WebhookStatusCode,
        [property: JsonPropertyName("dispatched_at")] DateTimeOffset DispatchedAt);
}
