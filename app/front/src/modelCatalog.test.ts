import { describe, expect, it } from "vitest";
import { DEFAULT_LLM_MODELS, mergeLlmModels, resolveInitialModelId } from "./modelCatalog";

describe("modelCatalog", () => {
  it("keeps the three Groq models and the studio model available by default", () => {
    expect(DEFAULT_LLM_MODELS.map((model) => model.id)).toEqual([
      "qwen-studio",
      "groq-gpt-oss-20b",
      "groq-gpt-oss-120b",
      "groq-qwen3-27b",
    ]);
  });

  it("falls back to the default catalog when the backend returns no models", () => {
    expect(mergeLlmModels([])).toEqual(DEFAULT_LLM_MODELS);
  });

  it("prefers backend labels while preserving missing default options", () => {
    const models = mergeLlmModels([
      { id: "groq-gpt-oss-20b", label: "GPT OSS 20B custom" },
    ]);

    expect(models).toContainEqual({ id: "groq-gpt-oss-20b", label: "GPT OSS 20B custom" });
    expect(models.map((model) => model.id)).toContain("qwen-studio");
    expect(models.map((model) => model.id)).toContain("groq-gpt-oss-120b");
    expect(models.map((model) => model.id)).toContain("groq-qwen3-27b");
  });

  it("uses a persisted model only when it is still available", () => {
    expect(resolveInitialModelId(DEFAULT_LLM_MODELS, "groq-qwen3-27b", "")).toBe("groq-qwen3-27b");
    expect(resolveInitialModelId(DEFAULT_LLM_MODELS, "missing-model", "")).toBe("qwen-studio");
  });
});
