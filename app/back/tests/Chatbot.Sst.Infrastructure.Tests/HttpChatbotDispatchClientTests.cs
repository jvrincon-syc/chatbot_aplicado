using System.Net;
using System.Text;
using System.Text.Json;
using Chatbot.Sst.Domain;
using Chatbot.Sst.Infrastructure.Dispatch;
using Microsoft.Extensions.Options;

namespace Chatbot.Sst.Infrastructure.Tests;

public sealed class HttpChatbotDispatchClientTests
{
    [Fact]
    public async Task DispatchAsync_resolves_release_from_paginated_items_using_project_id_template()
    {
        var handler = new RecordingHandler();
        var client = Build(handler, new ChatbotDispatchOptions
        {
            BaseUrl = "http://localhost:8000",
            BearerToken = "token",
            ProjectId = "proj_sst-general",
            RagVariantId = "ragv_local-bge",
            SubmitPath = "/api/chatbot/questions",
            ReleasesPathTemplate = "/api/platform/projects/{project_id}/releases?page=1&page_size=100",
            DefaultTopK = 10,
            RequestTimeoutSeconds = 60
        });

        var receipt = await client.DispatchAsync(
            new ChatQuestionSubmission(
                "Que establece la politica de seguridad y salud en el trabajo?",
                "conv_123",
                "msg_456",
                8),
            CancellationToken.None);

        Assert.Equal(
            "/api/platform/projects/proj_sst-general/releases?page=1&page_size=100",
            handler.Requests[0].PathAndQuery);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("Bearer token", handler.Requests[0].Authorization);

        Assert.Equal("/api/chatbot/questions", handler.Requests[1].PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal("Bearer token", handler.Requests[1].Authorization);

        using var body = JsonDocument.Parse(handler.Requests[1].Body);
        Assert.Equal("proj_sst-general", body.RootElement.GetProperty("project_id").GetString());
        Assert.Equal("ragv_local-bge", body.RootElement.GetProperty("rag_variant_id").GetString());
        Assert.Equal("ragr_published_002", body.RootElement.GetProperty("rag_release_id").GetString());
        Assert.Equal("msg_456", body.RootElement.GetProperty("message_id").GetString());
        Assert.Equal(8, body.RootElement.GetProperty("top_k").GetInt32());

        Assert.Equal("chatq_001", receipt.DispatchId);
        Assert.Equal("ragr_published_002", receipt.RagReleaseId);
        Assert.Equal("msg_456", receipt.MessageId);
    }

    [Fact]
    public async Task DispatchAsync_reuses_cached_release_across_calls_instead_of_refetching()
    {
        var handler = new RecordingHandler();
        var client = Build(handler, new ChatbotDispatchOptions
        {
            BaseUrl = "http://localhost:8000",
            BearerToken = "token",
            ProjectId = "proj_sst-general",
            RagVariantId = "ragv_local-bge",
            SubmitPath = "/api/chatbot/questions",
            ReleasesPathTemplate = "/api/platform/projects/{project_id}/releases?page=1&page_size=100",
            DefaultTopK = 10,
            RequestTimeoutSeconds = 60
        });

        await client.DispatchAsync(
            new ChatQuestionSubmission("question one", "conv_123", "msg_1", 8),
            CancellationToken.None);
        await client.DispatchAsync(
            new ChatQuestionSubmission("question two", "conv_123", "msg_2", 8),
            CancellationToken.None);

        var releaseLookups = handler.Requests.Count(r => r.Method == HttpMethod.Get);
        var dispatches = handler.Requests.Count(r => r.Method == HttpMethod.Post);

        Assert.Equal(1, releaseLookups);
        Assert.Equal(2, dispatches);
    }

    [Fact]
    public async Task ListRagReleasesAsync_parses_scoped_releases_and_orders_by_release_number_desc()
    {
        var handler = new RagReleasesHandler();
        var client = Build(handler, new ChatbotDispatchOptions
        {
            BaseUrl = "http://localhost:8000",
            BearerToken = "token",
            ProjectId = "proj_sst-general",
            RagVariantId = "ragv_local-bge",
            SubmitPath = "/api/chatbot/questions",
            ReleasesPathTemplate = "/api/platform/projects/{project_id}/releases?page=1&page_size=100",
            RagReleasesPathTemplate = "/api/chatbot/rag-releases?project_id={project_id}&rag_variant_id={rag_variant_id}",
            DefaultTopK = 10,
            RequestTimeoutSeconds = 60
        });

        var releases = await client.ListRagReleasesAsync(CancellationToken.None);

        Assert.Equal(
            "/api/chatbot/rag-releases?project_id=proj_sst-general&rag_variant_id=ragv_local-bge",
            handler.RequestedPathAndQuery);
        Assert.Equal("Bearer token", handler.RequestedAuthorization);

        Assert.Equal(2, releases.Count);
        Assert.Equal("ragr_published_002", releases[0].RagReleaseId);
        Assert.Equal(2, releases[0].ReleaseNumber);
        Assert.True(releases[0].ValidatedAt.HasValue);
        Assert.Equal("ragr_old_001", releases[1].RagReleaseId);
        Assert.Null(releases[1].ValidatedAt);
    }

    private sealed class RagReleasesHandler : HttpMessageHandler
    {
        public string? RequestedPathAndQuery { get; private set; }
        public string? RequestedAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedPathAndQuery = request.RequestUri?.PathAndQuery;
            RequestedAuthorization = request.Headers.Authorization?.ToString();

            const string json = """
            [
              {
                "rag_release_id": "ragr_old_001",
                "project_id": "proj_sst-general",
                "rag_variant_id": "ragv_local-bge",
                "state": "draft",
                "release_number": 1,
                "created_at": "2026-08-25T10:00:00Z"
              },
              {
                "rag_release_id": "ragr_published_002",
                "project_id": "proj_sst-general",
                "rag_variant_id": "ragv_local-bge",
                "state": "published",
                "release_number": 2,
                "created_at": "2026-08-27T09:00:00Z",
                "validated_at": "2026-08-27T09:30:00Z"
              }
            ]
            """;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private static HttpChatbotDispatchClient Build(HttpMessageHandler handler, ChatbotDispatchOptions options)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri(options.BaseUrl) };
        return new HttpChatbotDispatchClient(http, Options.Create(options));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri?.PathAndQuery ?? string.Empty,
                request.Headers.Authorization?.ToString(),
                body));

            if (request.Method == HttpMethod.Get)
            {
                const string releasesJson = """
                {
                  "items": [
                    {
                      "rag_release_id": "ragr_old_001",
                      "project_id": "proj_sst-general",
                      "rag_variant_id": "ragv_local-bge",
                      "state": "draft",
                      "release_number": 1,
                      "created_at": "2026-08-25T10:00:00Z"
                    },
                    {
                      "rag_release_id": "ragr_published_002",
                      "project_id": "proj_sst-general",
                      "rag_variant_id": "ragv_local-bge",
                      "state": "published",
                      "release_number": 2,
                      "created_at": "2026-08-27T09:00:00Z"
                    }
                  ],
                  "page": 1,
                  "page_size": 100,
                  "total_items": 2,
                  "total_pages": 1
                }
                """;

                return Json(HttpStatusCode.OK, releasesJson);
            }

            const string dispatchJson = """
            {
              "dispatch_id": "chatq_001",
              "project_id": "proj_sst-general",
              "rag_variant_id": "ragv_local-bge",
              "rag_release_id": "ragr_published_002",
              "retrieval_profile_id": "retrieval-profile-001",
              "question": "Que establece la politica de seguridad y salud en el trabajo?",
              "conversation_id": "conv_123",
              "message_id": "msg_456",
              "chunks_sent": 8,
              "webhook_status_code": 202,
              "dispatched_at": "2026-08-27T12:00:00Z"
            }
            """;

            return Json(HttpStatusCode.Accepted, dispatchJson);
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
            => new(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string PathAndQuery,
        string? Authorization,
        string Body);
}
