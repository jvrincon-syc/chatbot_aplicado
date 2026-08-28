import { useChat } from "../../hooks/useChat";
import { ChatHeader } from "../../components/ChatHeader";
import { MessageList } from "../../components/MessageList";
import { ChatInput } from "../../components/ChatInput";
import { ErrorBanner } from "../../components/ErrorBanner";

const SUGGESTIONS = [
  "¿Qué es un ATS?",
  "¿Cada cuánto se revisa el extintor?",
  "Elementos de protección para trabajo en alturas",
];

export function ChatPage() {
  const { messages, loading, error, send, retry } = useChat();
  const empty = messages.length === 0 && !loading;

  return (
    <main className="chat">
      <ChatHeader title="Chatbot SST" subtitle="Respuestas basadas en tus documentos de seguridad y salud." />

      <section className="chat__body">
        {empty ? (
          <div className="welcome">
            <p className="welcome__lead">Pregunta lo que necesites saber.</p>
            <div className="welcome__chips">
              {SUGGESTIONS.map((s) => (
                <button key={s} type="button" className="suggestion" onClick={() => send(s)}>
                  {s}
                </button>
              ))}
            </div>
          </div>
        ) : (
          <MessageList messages={messages} busy={loading} />
        )}
      </section>

      <ErrorBanner error={error} onRetry={retry} />
      <ChatInput onSend={send} disabled={loading} />
    </main>
  );
}
