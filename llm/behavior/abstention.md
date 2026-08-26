# Abstention (fail-closed policy)

The LLM is not an authoritative source. Conversation history is not evidence.

## Invariant

```
insufficient evidence  →  NO LLM invocation  →  deterministic abstention
```

When the evidence sufficiency gate fails, the backend returns a fixed message **without calling the
model**. This protects both latency and factual correctness.

## Canonical message

```
No encontré información suficiente en los documentos disponibles para responder esta pregunta con certeza.
```

Defined once in code as `ChatResponse.AbstentionMessage` / `ChatResponse.Abstention()`
(`Chatbot.Sst.Domain`). Do not duplicate the string elsewhere. Wording may become configurable
later, but the invariant (no LLM on insufficient evidence) does not change.
