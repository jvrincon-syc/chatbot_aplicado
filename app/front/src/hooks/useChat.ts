import { useCallback, useRef, useState } from "react";
import { getChatRequest, startChat } from "../api/chat";
import { ApiError } from "../api/client";
import type { ChatError, ChatMessage, ChatRequestChunk, ChatRequestStatus, Citation } from "../types";

const POLL_INTERVAL_MS = 1000;
const MAX_POLL_DURATION_MS = 210_000;
const CLIENT_POLL_TIMEOUT_CODE = "CHATBOT_CLIENT_POLL_TIMEOUT";

const sleep = (ms: number) => new Promise((resolve) => window.setTimeout(resolve, ms));

function toChatError(error: unknown, requestId?: string | null): ChatError {
  if (error instanceof ApiError) {
    return {
      code: error.code ?? `HTTP_${error.status}`,
      message: error.message,
      requestId,
    };
  }

  return {
    code: "CHATBOT_UNKNOWN_FAILURE",
    message: "Ocurrio un error inesperado.",
    requestId,
  };
}

function toAssistantMessage(status: ChatRequestStatus): ChatMessage {
  const citationLabelsByDocumentId = indexCitationLabels(status.chunks);
  return {
    id: crypto.randomUUID(),
    role: "assistant",
    text: status.answer ?? "No se recibio respuesta final del backend local.",
    abstained: status.abstained ?? false,
    citations: (status.citations ?? []).map((citation) => ({
      ...citation,
      documentTitle: resolveCitationTitle(citation, citationLabelsByDocumentId),
    })),
  };
}

function indexCitationLabels(
  chunks: ChatRequestStatus["chunks"],
): ReadonlyMap<string, string> {
  const labels = new Map<string, string>();
  for (const chunk of chunks ?? []) {
    const label = readChunkCitationLabel(chunk);
    if (label && !labels.has(chunk.documentId)) {
      labels.set(chunk.documentId, label);
    }
  }

  return labels;
}

function readChunkCitationLabel(chunk: ChatRequestChunk): string | null {
  const metadata = chunk.metadata;
  if (!metadata) {
    return null;
  }

  const preferred = metadata["citation_label"]?.trim();
  if (preferred) {
    return preferred;
  }

  const fallback = metadata["document_name"]?.trim();
  return fallback || null;
}

function resolveCitationTitle(
  citation: Citation,
  citationLabelsByDocumentId: ReadonlyMap<string, string>,
): string {
  const explicitLabel = citationLabelsByDocumentId.get(citation.documentId);
  if (explicitLabel) {
    return explicitLabel;
  }

  const title = citation.documentTitle?.trim();
  return title || citation.documentId;
}

// All chat state + orchestration lives here so any screen can reuse it.
export function useChat() {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ChatError | null>(null);
  const [conversationId] = useState(() => `conv_${crypto.randomUUID().replaceAll("-", "")}`);
  const lastQuestionRef = useRef<string | null>(null);
  const lastPendingRequestIdRef = useRef<string | null>(null);

  const settleStatus = useCallback((status: ChatRequestStatus) => {
    lastPendingRequestIdRef.current = null;

    if (status.state === "failed") {
      setError({
        code: status.errorCode ?? "CHATBOT_UNKNOWN_FAILURE",
        message: status.error ?? "No se pudo completar la conversacion.",
        chunksSent: status.chunksSent,
        requestId: status.requestId,
      });
      return;
    }

    if (status.state === "completed") {
      setMessages((prev) => [...prev, toAssistantMessage(status)]);
    }
  }, []);

  const resumePendingRequest = useCallback(async (requestId: string) => {
    setError(null);
    setLoading(true);

    try {
      const status = await waitForCompletion(requestId);
      settleStatus(status);
    } catch (error) {
      setError(toChatError(error, requestId));
    } finally {
      setLoading(false);
    }
  }, [settleStatus]);

  const send = useCallback(async (text: string) => {
    const trimmed = text.trim();
    if (!trimmed || loading) return;

    lastQuestionRef.current = trimmed;
    lastPendingRequestIdRef.current = null;
    setError(null);
    setMessages((prev) => [...prev, { id: crypto.randomUUID(), role: "user", text: trimmed }]);
    setLoading(true);

    try {
      let status = await startChat(trimmed, { conversationId });
      if (status.state === "pending") {
        lastPendingRequestIdRef.current = status.requestId;
        status = await waitForCompletion(status.requestId);
      }

      settleStatus(status);
    } catch (error) {
      setError(toChatError(error, lastPendingRequestIdRef.current));
    } finally {
      setLoading(false);
    }
  }, [conversationId, loading, settleStatus]);

  const retry = useCallback(() => {
    if (loading) return;

    const pendingRequestId = lastPendingRequestIdRef.current;
    if (pendingRequestId) {
      void resumePendingRequest(pendingRequestId);
      return;
    }

    if (lastQuestionRef.current) {
      void send(lastQuestionRef.current);
    }
  }, [loading, resumePendingRequest, send]);

  return { messages, loading, error, send, retry };
}

async function waitForCompletion(requestId: string): Promise<ChatRequestStatus> {
  const deadline = Date.now() + MAX_POLL_DURATION_MS;

  while (Date.now() < deadline) {
    const status = await getChatRequest(requestId);
    if (status.state !== "pending") {
      return status;
    }

    await sleep(POLL_INTERVAL_MS);
  }

  throw new ApiError(
    0,
    "La respuesta tarda mas de lo normal, pero la solicitud sigue en proceso.",
    CLIENT_POLL_TIMEOUT_CODE,
  );
}
