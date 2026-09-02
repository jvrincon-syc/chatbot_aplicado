# Graph Report - app  (2026-09-02)

## Corpus Check
- 58 files · ~14,360 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 460 nodes · 871 edges · 16 communities (13 shown, 1 thin omitted)
- Extraction: 95% EXTRACTED · 5% INFERRED · 0% AMBIGUOUS · INFERRED: 40 edges (avg confidence: 0.85)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- LLM Provider
- Dispatch Coordinator & Store
- Frontend Chat API Client
- Dispatch Client & Errors
- Chat Event Stream (Redis/In-Memory)
- API Composition & Prompt
- HTTP Chat Contracts
- Query Normalizer & DI
- Chat Service & Streaming
- Frontend App & Auth
- Project Files (.csproj)
- Launch Settings
- Health Checks & Secrets Loader
- Answer Formatter

## God Nodes (most connected - your core abstractions)
1. `ChatRequestSnapshot` - 28 edges
2. `HttpChatbotDispatchClient` - 24 edges
3. `ChatDispatchCoordinator` - 22 edges
4. `Chatbot.Sst.Application.Abstractions` - 20 edges
5. `Chatbot.Sst.Domain` - 17 edges
6. `OpenAiCompatibleLlmProvider` - 16 edges
7. `ChatWebhookDelivery` - 15 edges
8. `InMemoryChatRequestStore` - 14 edges
9. `ChatCompletionRequest` - 14 edges
10. `WebhookChunk` - 13 edges

## Surprising Connections (you probably didn't know these)
- `ChatDispatchCoordinator` --references--> `IChatEventStream`  [EXTRACTED]
  back/src/Chatbot.Sst.Application/ChatDispatchCoordinator.cs → back/src/Chatbot.Sst.Application/Abstractions/IChatEventStream.cs
- `ChatDispatchCoordinator` --references--> `IChatService`  [EXTRACTED]
  back/src/Chatbot.Sst.Application/ChatDispatchCoordinator.cs → back/src/Chatbot.Sst.Application/Abstractions/IChatService.cs
- `ChatDispatchCoordinator` --references--> `IChatbotDispatchClient`  [EXTRACTED]
  back/src/Chatbot.Sst.Application/ChatDispatchCoordinator.cs → back/src/Chatbot.Sst.Application/Abstractions/IChatbotDispatchClient.cs
- `GroundedAnswerService` --references--> `ILlmProvider`  [EXTRACTED]
  back/src/Chatbot.Sst.Application/GroundedAnswerService.cs → back/src/Chatbot.Sst.Application/Abstractions/ILlmProvider.cs
- `LlmHealthCheck` --references--> `ILlmProvider`  [EXTRACTED]
  back/src/Chatbot.Sst.Infrastructure/Llm/LlmHealthCheck.cs → back/src/Chatbot.Sst.Application/Abstractions/ILlmProvider.cs

## Import Cycles
- None detected.

## Communities (16 total, 1 thin omitted)

### Community 0 - "LLM Provider"
Cohesion: 0.05
Nodes (50): ILlmProvider, LlmRequest, MaxOutputTokens, StopSequences, Temperature, LlmResponse, CompletionTokens, PromptTokens (+42 more)

### Community 1 - "Dispatch Coordinator & Store"
Cohesion: 0.09
Nodes (21): IChatDispatchCoordinator, CancellationToken, Task, IChatRequestStore, ChatDispatchCoordinator, CancellationToken, ChatResponse, Exception (+13 more)

### Community 2 - "Frontend Chat API Client"
Cohesion: 0.08
Nodes (40): ChatStreamHandlers, getChatRequest(), startChat(), StartChatOptions, streamChat(), ApiError, getJson(), postJson() (+32 more)

### Community 3 - "Dispatch Client & Errors"
Cohesion: 0.11
Nodes (25): ChatDispatchException, ErrorCode, StatusCode, IChatbotDispatchClient, CancellationToken, IReadOnlyList, Task, PublishedRelease (+17 more)

### Community 4 - "Chat Event Stream (Redis/In-Memory)"
Cohesion: 0.08
Nodes (26): ChatStreamEvent, IChatEventStream, CancellationToken, IAsyncEnumerable, Task, RedisChatEventStream, CancellationToken, IAsyncEnumerable (+18 more)

### Community 5 - "API Composition & Prompt"
Cohesion: 0.10
Nodes (16): Program, EvidencePromptBuilder, LlmHealthCheck, CancellationToken, Task, Chatbot.Sst.Application.Abstractions, Chatbot.Sst.Infrastructure.Streaming, Chatbot.Sst.Application (+8 more)

### Community 6 - "HTTP Chat Contracts"
Cohesion: 0.09
Nodes (19): ChatContractHelpers, ChatRequestChunkResponse, ChatRequestStatusResponse, ChatWebhookChunkRequest, ChatWebhookRequest, RagReleaseResponse, StartChatRequest, DateTimeOffset (+11 more)

### Community 7 - "Query Normalizer & DI"
Cohesion: 0.06
Nodes (30): IQueryNormalizer, DefaultQueryNormalizer, Regex, GenerationOptions, EvidenceTokenBudget, DependencyInjection, IConnectionMultiplexer, ILogger (+22 more)

### Community 8 - "Chat Service & Streaming"
Cohesion: 0.11
Nodes (21): ChatAnswerChunk, IsFinal, IChatService, CancellationToken, IAsyncEnumerable, Task, LlmMessage, IReadOnlyList (+13 more)

### Community 9 - "Frontend App & Auth"
Cohesion: 0.13
Nodes (18): App(), AuthContext, AuthCtx, AuthProvider(), AuthState, loadUser(), useAuth(), User (+10 more)

### Community 10 - "Project Files (.csproj)"
Cohesion: 0.11
Nodes (15): net10.0, net10.0, Microsoft.NET.Sdk, net10.0, Microsoft.NET.Sdk, net10.0, Microsoft.NET.Sdk, Microsoft.AspNetCore.OpenApi (10.0.11) (+7 more)

### Community 11 - "Launch Settings"
Cohesion: 0.13
Nodes (15): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+7 more)

### Community 12 - "Health Checks & Secrets Loader"
Cohesion: 0.24
Nodes (4): HealthEndpointPredicates, SecretsEnvLoader, Chatbot.Sst.Api, HealthCheckRegistration

## Knowledge Gaps
- **95 isolated node(s):** `net10.0`, `Microsoft.AspNetCore.OpenApi (10.0.11)`, `Microsoft.NET.Sdk.Web`, `Program`, `$schema` (+90 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 157 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **1 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `OpenAiCompatibleLlmProvider` connect `LLM Provider` to `API Composition & Prompt`, `Query Normalizer & DI`?**
  _High betweenness centrality (0.101) - this node is a cross-community bridge._
- **Why does `HttpChatbotDispatchClient` connect `Dispatch Client & Errors` to `API Composition & Prompt`, `Query Normalizer & DI`?**
  _High betweenness centrality (0.081) - this node is a cross-community bridge._
- **Why does `ChatDispatchCoordinator` connect `Dispatch Coordinator & Store` to `Dispatch Client & Errors`, `Chat Event Stream (Redis/In-Memory)`, `API Composition & Prompt`, `Query Normalizer & DI`, `Chat Service & Streaming`?**
  _High betweenness centrality (0.065) - this node is a cross-community bridge._
- **What connects `net10.0`, `Microsoft.AspNetCore.OpenApi (10.0.11)`, `Microsoft.NET.Sdk.Web` to the rest of the system?**
  _95 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `LLM Provider` be split into smaller, more focused modules?**
  _Cohesion score 0.05021173623714459 - nodes in this community are weakly interconnected._
- **Should `Dispatch Coordinator & Store` be split into smaller, more focused modules?**
  _Cohesion score 0.0936408106219427 - nodes in this community are weakly interconnected._
- **Should `Frontend Chat API Client` be split into smaller, more focused modules?**
  _Cohesion score 0.07616892911010557 - nodes in this community are weakly interconnected._