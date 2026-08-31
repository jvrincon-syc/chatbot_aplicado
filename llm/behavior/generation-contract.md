# Generation contract

What the local LLM receives, and what it must never receive. The final versioned contract is
designed later; this is the direction.

## Payload shape

```
SYSTEM:   <static system prompt - see system-prompt.md>
QUESTION: <normalized user question>
EVIDENCE:
  [SOURCE 1] Document / Page / Section + text
  [SOURCE 2] ...
```

## Token budget (2048 ctx - do not fill it)

| Part                | Target      |
|---------------------|-------------|
| system instructions | ~100-180    |
| question            | ~20-60      |
| evidence            | ~300-700    |
| output              | <=200       |

Many candidates are retrieved; only the top reranked, budget-bounded fragments reach the LLM.

## Must NOT be sent to the LLM

Full corpus - all raw candidates - DB internals / physical table names - embedding vectors -
internal release details - unnecessary scores - secrets - connection strings.

## Output

Deterministic (`temperature 0`, `reasoning off`). The backend attaches citations from the evidence,
not from the model's free text. The model should return plain text only: no markdown headings,
no bold markers, and no `Fuentes`/`Sources` block because source chips are rendered separately.
