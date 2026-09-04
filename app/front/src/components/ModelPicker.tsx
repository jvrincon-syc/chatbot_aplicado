import type { LlmModel } from "../api/chat";

interface ModelPickerProps {
  models: readonly LlmModel[];
  selectedModel: string;
  disabled?: boolean;
  onChange: (modelId: string) => void;
}

export function ModelPicker({ models, selectedModel, disabled, onChange }: ModelPickerProps) {
  return (
    <label className="model-picker">
      <span className="model-picker__label">Modelo</span>
      <select
        className="model-picker__select"
        aria-label="Modelo de IA"
        value={selectedModel}
        disabled={disabled || models.length === 0}
        onChange={(e) => onChange(e.target.value)}
      >
        {models.map((model) => (
          <option key={model.id} value={model.id}>
            {model.label}
          </option>
        ))}
      </select>
    </label>
  );
}
