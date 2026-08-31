import { useChat } from "../../hooks/useChat";
import { Sidebar } from "../../components/Sidebar";
import { MessageList } from "../../components/MessageList";
import { ChatInput } from "../../components/ChatInput";
import { ErrorBanner } from "../../components/ErrorBanner";

const ICON_SHIELD_CHECK = '<svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"/><path d="m9 12 2 2 4-4"/></svg>';

const SUGGESTIONS = [
  "¿Qué es un ATS?",
  "¿Cada cuánto se revisa el extintor?",
  "Elementos de protección para trabajo en alturas",
];

export function ChatPage() {
  const { messages, loading, error, send, retry } = useChat();
  const empty = messages.length === 0 && !loading;

  return (
    <div className="app">
      <div className="app__topbar" aria-hidden="true" />
      <div className="app__body">
        <Sidebar />

        <main className="main">
          <div className="main__workspace">
            <div className="main__top">
              <span className="main__context-badge">
                <span className="main__context-dot" />
                Marco Legal SST Colombia
              </span>
            </div>

            {empty ? (
              <div className="welcome">
                <div className="welcome__icon" dangerouslySetInnerHTML={{ __html: ICON_SHIELD_CHECK }} />
                <p className="welcome__lead">
                  Tu asistente experto en
                  <br />
                  Seguridad y Salud en el Trabajo
                </p>
                <p className="welcome__sub">
                  Respuestas confiables. Información verificada. Entornos más seguros.
                </p>
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
          </div>

          <ErrorBanner error={error} onRetry={retry} />
          <ChatInput onSend={send} disabled={loading} />
        </main>
      </div>
    </div>
  );
}
