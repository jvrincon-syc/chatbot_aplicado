import type { LlmModel } from "./api/chat";

export const DEFAULT_LLM_MODELS: readonly LlmModel[] = [
  { id: "qwen-studio", label: "Qwen - Lightning/LM Studio" },
  { id: "groq-gpt-oss-20b", label: "GPT-OSS 20B - Groq" },
  { id: "groq-gpt-oss-120b", label: "GPT-OSS 120B - Groq" },
  { id: "groq-qwen3-27b", label: "Qwen3 27B - Groq" },
];

export function mergeLlmModels(backendModels: readonly LlmModel[]): LlmModel[] {
  const merged = new Map<string, LlmModel>();

  for (const model of DEFAULT_LLM_MODELS) {
    merged.set(model.id, model);
  }

  for (const model of backendModels) {
    const id = model.id.trim();
    if (!id) {
      continue;
    }

    merged.set(id, {
      id,
      label: model.label.trim() || id,
    });
  }

  return [...merged.values()];
}

export function resolveInitialModelId(
  models: readonly LlmModel[],
  persistedModelId: string | null,
  currentModelId: string,
): string {
  const availableIds = new Set(models.map((model) => model.id));
  if (persistedModelId && availableIds.has(persistedModelId)) {
    return persistedModelId;
  }

  if (currentModelId && availableIds.has(currentModelId)) {
    return currentModelId;
  }

  return models[0]?.id ?? "";
}
