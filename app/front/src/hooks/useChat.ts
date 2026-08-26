import { useCallback, useState } from "react";
import { sendChat } from "../api/chat";
import { ApiError } from "../api/client";
import type { ChatMessage } from "../types";

// All chat state + orchestration lives here so any screen can reuse it.
export function useChat() {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const send = useCallback(async (text: string) => {
    const trimmed = text.trim();
    if (!trimmed || loading) return;

    setError(null);
    setMessages((prev) => [...prev, { id: crypto.randomUUID(), role: "user", text: trimmed }]);
    setLoading(true);
    try {
      const res = await sendChat(trimmed);
      setMessages((prev) => [
        ...prev,
        {
          id: crypto.randomUUID(),
          role: "assistant",
          text: res.answer,
          abstained: res.abstained,
          citations: res.citations,
        },
      ]);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Ocurrió un error inesperado.");
    } finally {
      setLoading(false);
    }
  }, [loading]);

  return { messages, loading, error, send };
}
