import { useState } from "react";

// Reusable composer. Submits on Enter; disabled while busy.
export function ChatInput({ onSend, disabled }: { onSend: (text: string) => void; disabled?: boolean }) {
  const [text, setText] = useState("");

  const submit = () => {
    if (!text.trim() || disabled) return;
    onSend(text);
    setText("");
  };

  return (
    <form
      className="composer"
      onSubmit={(e) => {
        e.preventDefault();
        submit();
      }}
    >
      <label htmlFor="chat-input" className="sr-only">Tu pregunta</label>
      <input
        id="chat-input"
        className="composer__input"
        value={text}
        onChange={(e) => setText(e.target.value)}
        placeholder="Escribe tu pregunta…"
        autoComplete="off"
        disabled={disabled}
      />
      <button className="composer__send" type="submit" disabled={disabled || !text.trim()}>
        Enviar
      </button>
    </form>
  );
}
