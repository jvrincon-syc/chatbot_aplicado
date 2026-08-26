# Code Review Checklist

Report findings as `path:line — severity — what fails and when`. A change is done only when it
passes this list and the build/tests are green.

## Architecture boundary
- [ ] No RAG-authoring capability added (ingestion, chunking, embedding generation, indexing,
      release management). This is a consumer.
- [ ] Dependency direction respected: `Domain ← Application ← Infrastructure ← Api`.
- [ ] Controllers/endpoints hold no SQL, LLM calls, scoring, or orchestration rules.

## Reuse & simplicity (see `AGENTS.md`)
- [ ] No re-implementation of an existing helper/port/model/component — searched first.
- [ ] Cross-module logic is shared, not duplicated; bugs fixed at the shared root.
- [ ] Interfaces are small and capability-specific (no monolithic `IAiService`).
- [ ] Nothing longer than it needs to be; one-liners where they read clearly.

## Correctness
- [ ] Fails closed on missing config, insufficient evidence, or unreachable dependency.
- [ ] External services accessed only through ports; no leaking infra types upward.
- [ ] Nullability honored; no swallowed exceptions; cancellation tokens propagated.
- [ ] Non-trivial logic has a runnable test; edge cases covered.

## Security & data
- [ ] Meets every rule in `SECURITY_AND_DATA.md` (secrets, RAG scope, read-only DB, LLM exposure,
      logging).
- [ ] No secrets/weights added to Git; no trusted RAG IDs from the client.

## Hygiene
- [ ] Build is 0 warnings / 0 errors; tests pass and failures are reported, not hidden.
- [ ] Names, comment density, and idiom match surrounding code.
