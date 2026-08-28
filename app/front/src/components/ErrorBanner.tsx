import type { ChatError } from "../types";

// Known errorCode -> user-facing headline. Unrecognized codes fall back to a generic
// headline below, but the raw backend message is still shown.
const ERROR_MESSAGES: Record<string, string> = {
  CHATBOT_LLM_UNAVAILABLE:
    "El modelo de IA local no esta disponible en este momento. Intenta de nuevo en unos segundos.",
  CHATBOT_LLM_TIMEOUT: "El modelo de IA local tardo demasiado en responder. Intenta de nuevo.",
  CHATBOT_LLM_DELIVERY_FAILED: "El modelo de IA local devolvio una respuesta invalida o incompleta.",
  CHATBOT_CLIENT_POLL_TIMEOUT:
    "La respuesta sigue en proceso y esta tardando mas de lo normal. Puedes reanudarla con Reintentar.",
  CHATBOT_DISPATCH_UNEXPECTED_FAILURE: "Ocurrio un error inesperado al procesar tu pregunta.",
  CHATBOT_RAG_CONTEXT_MISMATCH: "Los documentos consultados no coinciden con la configuracion esperada.",
  CHATBOT_RELEASE_NOT_PUBLISHED: "La version de documentos configurada aun no esta publicada.",
  CHATBOT_RELEASE_LANE_UNAVAILABLE: "No hay una version de documentos disponible en este momento.",
  CHATBOT_EVIDENCE_UNAVAILABLE: "No se encontro evidencia suficiente en los documentos para responder.",
  CHATBOT_WEBHOOK_NOT_CONFIGURED: "El servicio de busqueda de documentos no esta configurado.",
  CHATBOT_WEBHOOK_DELIVERY_FAILED: "No se pudo contactar al servicio de busqueda de documentos.",
  HTTP_AUTH_REQUIRED: "Debes iniciar sesion para continuar.",
  HTTP_AUTH_INVALID_CREDENTIALS: "Las credenciales ingresadas no son validas.",
};

const FALLBACK_HEADLINE = "Ocurrio un error al procesar tu pregunta.";

// System-level error notice (not an inline message bubble). Shows a friendly headline per
// known errorCode, the raw backend detail, whether retrieval succeeded before generation
// failed, and a retry affordance for the last question sent.
export function ErrorBanner({ error, onRetry }: { error: ChatError | null; onRetry?: () => void }) {
  if (!error) return null;

  const headline = ERROR_MESSAGES[error.code] ?? FALLBACK_HEADLINE;
  const hasChunks = typeof error.chunksSent === "number" && error.chunksSent > 0;
  const showRawMessage = !!error.message && error.message !== headline;

  return (
    <div className="error" role="alert">
      <p className="error__headline">{headline}</p>
      {hasChunks && (
        <p className="error__detail">
          Se encontraron {error.chunksSent} fragmentos relevantes, pero no se pudo generar la respuesta.
        </p>
      )}
      {showRawMessage && <p className="error__detail">{error.message}</p>}
      <p className="error__meta">
        Codigo: {error.code}
        {error.requestId ? ` · Solicitud: ${error.requestId}` : ""}
      </p>
      {onRetry && (
        <button type="button" className="error__retry" onClick={onRetry}>
          Reintentar
        </button>
      )}
    </div>
  );
}
