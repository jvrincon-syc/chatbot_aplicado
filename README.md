# chatbot-sst

End-user **chatbot** that answers questions grounded in an already-published RAG product, using a
local LLM. It is a **runtime consumer** — it does not build or manage RAGs.

```
User question → normalization → FAQ/Redis → PostgreSQL FTS → (vector fallback) → rerank
             → evidence budget → sufficiency gate → local LLM (grounded answer) | abstention
```

## Architecture boundary (important)

A separate **RAG Platform** repository owns the RAG lifecycle (ingestion, chunking, embeddings,
indexing, releases, publication). **This repo consumes the final product read-only** and must never
duplicate those responsibilities. Details: [`docs/architecture/system-boundary.md`](docs/architecture/system-boundary.md)
(planned), [`CLAUDE.md`](CLAUDE.md), and [`docs/rules/`](docs/rules/).

The consumed RAG is identified by a server-resolved `RagTarget(projectId, ragVariantId, ragReleaseId)`
— never trusted from the browser. Evidence policy is **fail-closed**: insufficient evidence produces
a deterministic abstention with no LLM call.

## Stack

| Layer | Tech |
|-------|------|
| Backend | C# / .NET 10 / ASP.NET Core (`app/back`, clean architecture) |
| Frontend | React + TypeScript SPA (`app/front`, not yet scaffolded) |
| Runtime infra (later) | PostgreSQL + pgvector, Redis, llama.cpp |
| Local LLM | Qwen3-1.7B GGUF IQ4_XS via `llama-server` (OpenAI-compatible HTTP) |
| Dev tooling only | Python 3.12 venv `.venv_tools/` (never a backend dependency) |

## Prerequisites

- .NET SDK 10+
- Node 20+ / npm (for the frontend, once added)
- `llama-server` (llama.cpp) on `PATH`
- The Qwen3-1.7B GGUF locally (external — **not** in this repo); path via `CHATBOT_LLM_MODEL_PATH`
- PostgreSQL + Redis (only when retrieval is implemented)

## Setup

```bash
# 1. Secrets & config
cp secrets.env.example secrets.env      # fill in locally; git-ignored

# 2. Backend
cd app/back
dotnet build Chatbot.Sst.slnx
dotnet test  Chatbot.Sst.slnx

# 3. Dev tooling venv (optional, HF CLI etc.) — from repo root
py -3.12 -m venv .venv_tools
```

## Run locally

```bash
# Local LLM (loopback only). Set CHATBOT_LLM_MODEL_PATH or rely on the documented default.
pwsh scripts/dev/start-llm.ps1

# Backend API (Development uses dummy RagTarget IDs in appsettings.Development.json)
cd app/back && dotnet run --project src/Chatbot.Sst.Api
```

Smoke-test the LLM path (Development only): `GET /health/llm` for reachability,
`POST /dev/llm/smoke` to exercise API → `ILlmProvider` → llama.cpp → Qwen. Runbook:
[`docs/runbooks/local-llm.md`](docs/runbooks/local-llm.md) (planned).

## Working in this repo

Read [`AGENTS.md`](AGENTS.md) (+ `app/back/AGENTS.md`, `app/front/AGENTS.md`) before contributing:
investigate and plan first, reuse existing helpers/components, keep changes small and layered, fail
closed, and follow [`docs/rules/`](docs/rules/).
