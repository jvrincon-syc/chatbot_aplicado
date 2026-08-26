import type { ChatMessage } from "../types";

// Reusable single message. Grounded answers show source chips; abstention is visually distinct.
export function MessageBubble({ message }: { message: ChatMessage }) {
  const isUser = message.role === "user";
  const hasCites = !isUser && !!message.citations?.length;

  return (
    <article className={`bubble bubble--${message.role}${message.abstained ? " bubble--abstained" : ""}`}>
      {message.abstained && <span className="bubble__badge">Sin evidencia suficiente</span>}
      <p className="bubble__text">{message.text}</p>

      {hasCites && (
        <div className="cites">
          <span className="cites__label">Fuentes</span>
          <ul className="cites__list">
            {message.citations!.map((c, i) => (
              <li key={i} className="chip">
                {c.documentTitle ?? c.documentId}
                {c.page ? <span className="chip__page">p.{c.page}</span> : null}
              </li>
            ))}
          </ul>
        </div>
      )}
    </article>
  );
}
