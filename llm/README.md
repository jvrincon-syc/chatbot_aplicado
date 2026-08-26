# llm/ — Local LLM module

Self-contained module for the local generation model: its **runtime configuration**, its
**behavior contract**, and its **launcher**. Everything that defines *what model runs and how it
behaves* lives here.

```
llm/
├── model.json              # single source of runtime config (model, server, generation, tuning)
├── start-llm.ps1           # launcher; reads model.json, validates the GGUF, binds 127.0.0.1
└── behavior/
    ├── system-prompt.md    # static system instructions
    ├── generation-contract.md  # what the LLM receives / must never receive + token budget
    └── abstention.md       # fail-closed policy + canonical message
```

## What lives here vs. in the backend

- **Here:** model identity, llama.cpp tuning, generation defaults, prompt/behavior docs, launcher.
- **In the backend (`app/back`):** the C# adapter `OpenAiCompatibleLlmProvider` implementing the
  `ILlmProvider` port. It talks to this model over OpenAI-compatible HTTP. The backend depends on
  the *port*, never on llama.cpp/Qwen concepts — keeping clean-architecture layering intact.

Keep the two in sync: `Llm:BaseUrl` (backend config) must match `server.host`/`server.port` here.

## Run

```bash
# Set the model path (external, never committed) or rely on model.json defaultPath:
export CHATBOT_LLM_MODEL_PATH="C:\path\to\Qwen_Qwen3-1.7B-IQ4_XS.gguf"

pwsh llm/start-llm.ps1
```

Stop with Ctrl+C. The server is loopback-only — never expose it to the LAN, and do not enable
llama.cpp agent/tool execution.

## Constraints (8 GB workstation)

Do not casually raise threads / context / parallel slots / GPU layers / batch sizes / generation
length in `model.json`. Keep `temperature 0` and `reasoning off`. A larger model (Qwen 4B) was
tested and was too heavy; 1.7B IQ4_XS is the accepted baseline.
