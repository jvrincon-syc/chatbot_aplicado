import type { ChatMessage } from "../types";

const ICON_FILE = '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"/><path d="M14 2v4a2 2 0 0 0 2 2h4"/></svg>';
const ICON_BOOKMARK = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m19 21-7-4-7 4V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v16z"/></svg>';
const ICON_THUMBS_UP = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 10v12"/><path d="M15 5.88 14 10h5.83a2 2 0 0 1 1.92 2.56l-2.33 8A2 2 0 0 1 17.5 22H4a2 2 0 0 1-2-2v-8a2 2 0 0 1 2-2h2.76a2 2 0 0 0 1.79-1.11L12 2h0a3.13 3.13 0 0 1 3 3.88"/></svg>';
const ICON_THUMBS_DOWN = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 14V2"/><path d="M9 18.12 10 14H4.17a2 2 0 0 1-1.92-2.56l2.33-8A2 2 0 0 1 6.5 2H20a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2h-2.76a2 2 0 0 0-1.79 1.11L12 22h0a3.13 3.13 0 0 1-3-3.88"/></svg>';
const ICON_SHIELD = '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"/><path d="m9 12 2 2 4-4"/></svg>';

export function MessageBubble({ message }: { message: ChatMessage }) {
  const isUser = message.role === "user";
  const hasCites = !isUser && !!message.citations?.length;

  return (
    <article className={`bubble bubble--${message.role}${message.abstained ? " bubble--abstained" : ""}`}>
      {message.abstained && <span className="bubble__badge">Sin evidencia suficiente</span>}

      {isUser ? (
        <p className="bubble__text">{message.text}</p>
      ) : (
        <div className="bubble--assistant-header">
          <div className="bubble--assistant-icon" aria-hidden="true" dangerouslySetInnerHTML={{ __html: ICON_SHIELD }} />
          <div className="bubble--assistant-body">
            <p className="bubble__text">{message.text}</p>
            <div className="bubble__actions">
              <button type="button" className="bubble__action" aria-label="Guardar" dangerouslySetInnerHTML={{ __html: ICON_BOOKMARK }} />
              <button type="button" className="bubble__action" aria-label="Útil" dangerouslySetInnerHTML={{ __html: ICON_THUMBS_UP }} />
              <button type="button" className="bubble__action" aria-label="No útil" dangerouslySetInnerHTML={{ __html: ICON_THUMBS_DOWN }} />
            </div>
          </div>
        </div>
      )}

      {hasCites && (
        <div className="cites">
          <span className="cites__label">Fuentes</span>
          <ul className="cites__list">
            {message.citations!.map((c, i) => (
              <li key={i} className="chip">
                <span dangerouslySetInnerHTML={{ __html: ICON_FILE }} />
                <span className="chip__text">
                  <span className="chip__title">{c.documentTitle ?? c.documentId}</span>
                  {(c.page || c.section) && (
                    <span className="chip__meta">
                      {c.section ? `Art. ${c.section}` : ""}
                      {c.page ? ` · p.${c.page}` : ""}
                    </span>
                  )}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </article>
  );
}
