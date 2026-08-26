import { useEffect, useRef } from "react";
import type { ChatMessage } from "../types";
import { MessageBubble } from "./MessageBubble";
import { TypingDots } from "./TypingDots";

// Reusable scrollable transcript. Auto-scrolls to the latest message.
export function MessageList({ messages, busy }: { messages: ChatMessage[]; busy?: boolean }) {
  const endRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, busy]);

  return (
    <div className="messages" role="log" aria-live="polite" aria-busy={busy}>
      {messages.map((m) => (
        <MessageBubble key={m.id} message={m} />
      ))}
      {busy && (
        <div className="bubble bubble--assistant bubble--typing">
          <TypingDots />
        </div>
      )}
      <div ref={endRef} />
    </div>
  );
}
