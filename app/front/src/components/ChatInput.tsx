import { useState } from "react";

const ICON_SEND = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z"/><path d="m21.854 2.147-10.94 10.939"/></svg>';

export function ChatInput({ onSend, disabled }: { onSend: (text: string) => void; disabled?: boolean }) {
  const [text, setText] = useState("");

  const submit = () => {
    if (!text.trim() || disabled) return;
    onSend(text);
    setText("");
  };

  return (
    <div className="composer-wrap">
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
          placeholder="Escribe tu pregunta sobre SST..."
          autoComplete="off"
          disabled={disabled}
        />
        <button className="composer__send" type="submit" disabled={disabled || !text.trim()} aria-label="Enviar" dangerouslySetInnerHTML={{ __html: ICON_SEND }} />
      </form>
      <p className="composer__disclaimer">
        Verifica siempre la información con tu área legal o profesional SST.
      </p>
    </div>
  );
}
