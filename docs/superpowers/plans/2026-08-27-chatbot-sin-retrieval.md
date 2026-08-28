# Chatbot Backend Relay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dejar este repositorio como frontend + backend de chatbot que envía preguntas al otro backend por `POST /api/chatbot/questions`, recibe `question + chunks` por webhook y consulta el LLM local solo con esos chunks.

**Architecture:** El backend local deja de modelar retrieval propio y pasa a ser un relay con estado de solicitud. El flujo correcto es: frontend envía la pregunta, backend resuelve una release `published` para `proj_sst-general` + `ragv_local-bge`, despacha la pregunta con bearer auth, recibe el webhook con `dispatch_id` y `chunks`, genera la respuesta grounded con el LLM local y expone un endpoint de polling para el frontend.

**Tech Stack:** ASP.NET Core Web API, C#/.NET 10, React 19 + TypeScript, `HttpClient`, `ConcurrentDictionary`.

**Spec:** `AGENTS.md`, `app/back/AGENTS.md`, `app/front/AGENTS.md`, `CLAUDE.md`

## Global Constraints

- Never add ingestion, chunking, embedding generation, indexing, or release-management logic here.
- React talks only to the ASP.NET Core API.
- Keep dependency direction `Domain <- Application <- Infrastructure <- API`.
- Use the external backend contract exactly:
  - `POST /api/chatbot/questions`
  - `Authorization: Bearer <token>`
  - strict JSON body with `project_id`, `rag_variant_id`, `rag_release_id`, `question`, `conversation_id`, `message_id`, `top_k`
- `project_id = proj_sst-general`
- `rag_variant_id = ragv_local-bge`
- `rag_release_id` must be resolved at runtime from `GET /api/platform/projects/proj_sst-general/releases`
- `top_k` allowed range is `1..25`, default `10`
- Never run tests from the terminal in this repository; hand commands to the user instead.

---

### Task 1: Remove Local Retrieval Placeholders

**Files:**
- Create: `app/back/src/Chatbot.Sst.Domain/Grounding.cs`
- Modify: `app/back/src/Chatbot.Sst.Application/GroundedAnswerService.cs`
- Modify: `app/back/src/Chatbot.Sst.Application/Abstractions/IQueryNormalizer.cs`
- Delete: `app/back/src/Chatbot.Sst.Domain/Retrieval.cs`
- Delete: `app/back/src/Chatbot.Sst.Domain/RagTarget.cs`
- Delete: `app/back/src/Chatbot.Sst.Application/Abstractions/IFaqService.cs`
- Delete: `app/back/src/Chatbot.Sst.Application/Abstractions/IQueryEmbeddingProvider.cs`
- Delete: `app/back/src/Chatbot.Sst.Application/Abstractions/IRagRetriever.cs`
- Delete: `app/back/src/Chatbot.Sst.Application/Abstractions/IRagTargetProvider.cs`
- Delete: `app/back/src/Chatbot.Sst.Application/Abstractions/IReranker.cs`

**Interfaces:**
- Consumes: `Task<ChatResponse> IChatService.AnswerAsync(UserQuestion question, EvidencePackage evidence, CancellationToken cancellationToken)`
- Produces: `Citation`, `Evidence`, `EvidencePackage` as grounding-only models

- [ ] **Step 1: Move grounding models out of retrieval terminology**
- [ ] **Step 2: Rewrite comments so the repo scope is webhook chunks + local LLM**
- [ ] **Step 3: Delete dead retrieval/RAG placeholder types and tests**
- [ ] **Step 4: Verify backend references compile cleanly**

### Task 2: Implement The External Dispatch Contract

**Files:**
- Create: `app/back/src/Chatbot.Sst.Domain/ChatDispatch.cs`
- Create: `app/back/src/Chatbot.Sst.Application/Abstractions/IChatbotDispatchClient.cs`
- Create: `app/back/src/Chatbot.Sst.Application/Abstractions/IChatRequestStore.cs`
- Create: `app/back/src/Chatbot.Sst.Application/Abstractions/IChatDispatchCoordinator.cs`
- Create: `app/back/src/Chatbot.Sst.Application/Abstractions/ChatDispatchException.cs`
- Create: `app/back/src/Chatbot.Sst.Application/ChatDispatchCoordinator.cs`
- Create: `app/back/src/Chatbot.Sst.Infrastructure/Dispatch/ChatbotDispatchOptions.cs`
- Create: `app/back/src/Chatbot.Sst.Infrastructure/Dispatch/HttpChatbotDispatchClient.cs`
- Create: `app/back/src/Chatbot.Sst.Infrastructure/Dispatch/InMemoryChatRequestStore.cs`
- Modify: `app/back/src/Chatbot.Sst.Infrastructure/DependencyInjection.cs`

**Interfaces:**
- Consumes: `Task<ChatDispatchReceipt> IChatbotDispatchClient.DispatchAsync(ChatQuestionSubmission submission, CancellationToken cancellationToken)`
- Consumes: `ChatRequestSnapshot IChatRequestStore.CreatePending(ChatQuestionSubmission submission)`
- Produces: local request snapshots keyed by `message_id`

- [ ] **Step 1: Generate or normalize `message_id` before dispatch to avoid webhook races**
- [ ] **Step 2: Resolve the published release from `/api/platform/projects/{project_id}/releases?page=1&page_size=100`**
- [ ] **Step 3: Dispatch the strict snake_case body to `/api/chatbot/questions` with bearer auth**
- [ ] **Step 4: Map upstream auth and contract errors into stable local error codes**
- [ ] **Step 5: Store pending/completed/failed request state for frontend polling**

### Task 3: Expose Local API + Frontend Polling

**Files:**
- Create: `app/back/src/Chatbot.Sst.Api/ChatContracts.cs`
- Modify: `app/back/src/Chatbot.Sst.Api/Program.cs`
- Modify: `app/back/src/Chatbot.Sst.Api/appsettings.json`
- Modify: `app/back/src/Chatbot.Sst.Api/appsettings.Development.json`
- Modify: `app/front/src/api/client.ts`
- Modify: `app/front/src/api/chat.ts`
- Modify: `app/front/src/hooks/useChat.ts`
- Modify: `app/front/src/types.ts`
- Modify: `app/back/src/Chatbot.Sst.Api/Chatbot.Sst.Api.http`
- Modify: `README.md`
- Modify: `AGENTS.md`
- Modify: `CLAUDE.md`
- Modify: `app/back/AGENTS.md`
- Modify: `app/front/AGENTS.md`

**Interfaces:**
- Produces: `POST /api/chat/requests`
- Produces: `GET /api/chat/requests/{requestId}`
- Produces: `POST /api/chat/webhook`

- [ ] **Step 1: Accept frontend submit requests with `question`, optional `conversationId`, optional `messageId`, optional `topK`**
- [ ] **Step 2: Accept webhook payloads in snake_case with `dispatch_id`, release identifiers, and `chunks`**
- [ ] **Step 3: Convert webhook chunks into `EvidencePackage` and query the local LLM**
- [ ] **Step 4: Update the frontend from direct chat call to submit + poll**
- [ ] **Step 5: Update docs and `.http` examples to the new contract**

### Task 4: Verify Builds Only

**Files:**
- Verify: `app/back`
- Verify: `app/front`

**Interfaces:**
- Consumes: local source tree after refactor
- Produces: confidence that the new relay contract compiles

- [ ] **Step 1: Run `cd app/back && dotnet build Chatbot.Sst.slnx`**
- [ ] **Step 2: Run `cd app/front && npm run build`**
- [ ] **Step 3: Hand test commands to the user instead of running them**

## Self-Review

1. **Spec coverage:** The plan removes local retrieval placeholders, implements the exact outbound contract, receives the webhook payload, and aligns the frontend with polling.
2. **Placeholder scan:** No `TODO`/`TBD` markers remain.
3. **Type consistency:** `project_id`, `rag_variant_id`, runtime `rag_release_id`, `message_id`, `dispatch_id`, and `top_k` are used consistently across the plan.
