# Security & Data Rules

Fail closed. If a rule below can't be satisfied, stop and surface it — never work around it.

## Secrets & weights
- No secrets, credentials, API tokens, or connection strings in Git, source, or the frontend bundle.
  Real values live only in `secrets.env` (git-ignored); `secrets.env.example` documents the shape.
- Never commit model weights (`*.gguf`). The model path is local config, never a code constant.

## RAG scope authority
- `projectId` / `ragVariantId` / `ragReleaseId` are resolved **server-side** from trusted config
  (`RagTarget`). Never trust these from the browser as authorization or scope.
- The backend fails closed if any identifier is missing (`ValidateOnStart`).
- Eventually the backend must verify release ∈ variant ∈ project and runtime-eligibility — do not
  invent those checks before the external RAG contract is confirmed.

## Data access
- The RAG product is consumed **read-only** with a dedicated reader identity
  (`chatbot_runtime_reader`, SELECT only). This repo never mutates RAG lifecycle state.
- Parameterized queries only. No string-interpolated/dynamic SQL, no user-controlled physical table
  names or absolute paths.

## Local LLM
- Bind `127.0.0.1` only — never expose to the LAN. No llama.cpp agent/tool execution.
- Keep `--temp 0` and `--reasoning off`. Don't raise resource limits (8 GB workstation).

## Evidence & LLM
- The LLM is not an authoritative source; conversation history is not evidence.
- Insufficient evidence ⇒ deterministic abstention **without invoking the LLM**.
- The LLM receives only short system instructions + question + a small evidence package — never the
  full corpus, DB internals, table names, vectors, scores, secrets, or connection strings.

## Logging
- Do not log by default: full document chunks, full prompts, full conversations, vectors, secrets,
  or connection strings. Structured logs use IDs/metrics, not content.

## Dependencies
- No compile-time dependency, submodule, or copied code from the RAG Platform. Prefer stdlib and
  already-installed packages before adding a new one.
