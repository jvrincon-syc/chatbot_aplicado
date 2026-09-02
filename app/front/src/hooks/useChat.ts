import { useCallback, useEffect, useRef, useState } from "react";
import { getModels, startChat, streamChat, type LlmModel } from "../api/chat";
import { ApiError } from "../api/client";
import type { ChatError, ChatMessage, ChatRequestChunk, ChatRequestStatus, Citation } from "../types";

const SOURCE_URL_METADATA_KEYS = [
  "source_url",
  "sourceUrl",
  "document_url",
  "documentUrl",
  "pdf_url",
  "pdfUrl",
  "file_url",
  "fileUrl",
  "url",
  "href",
];

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
  const citationUrlsByDocumentId = indexCitationSourceUrls(status.chunks);
  return {
    id: crypto.randomUUID(),
    role: "assistant",
    text: status.answer ?? "No se recibio respuesta final del backend local.",
    abstained: status.abstained ?? false,
    citations: (status.citations ?? []).map((citation) => ({
      ...citation,
      documentTitle: resolveCitationTitle(citation, citationLabelsByDocumentId),
      sourceUrl: resolveCitationSourceUrl(citation, citationUrlsByDocumentId),
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

function indexCitationSourceUrls(
  chunks: ChatRequestStatus["chunks"],
): ReadonlyMap<string, string> {
  const urls = new Map<string, string>();
  for (const chunk of chunks ?? []) {
    const url = readChunkCitationSourceUrl(chunk);
    if (url && !urls.has(chunk.documentId)) {
      urls.set(chunk.documentId, url);
    }
  }

  return urls;
}

function readChunkCitationSourceUrl(chunk: ChatRequestChunk): string | null {
  const metadata = chunk.metadata;
  if (!metadata) {
    return null;
  }

  for (const key of SOURCE_URL_METADATA_KEYS) {
    const url = normalizeHttpUrl(metadata[key]);
    if (url) {
      return url;
    }
  }

  return null;
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

function resolveCitationSourceUrl(
  citation: Citation,
  citationUrlsByDocumentId: ReadonlyMap<string, string>,
): string | null {
  return normalizeHttpUrl(citation.sourceUrl)
    ?? citationUrlsByDocumentId.get(citation.documentId)
    ?? null;
}

function normalizeHttpUrl(value?: string | null): string | null {
  const trimmed = value?.trim();
  if (!trimmed) {
    return null;
  }

  try {
    const url = new URL(trimmed);
    return url.protocol === "http:" || url.protocol === "https:" ? trimmed : null;
  } catch {
    return null;
  }
}

// All chat state + orchestration lives here so any screen can reuse it.
export function useChat() {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ChatError | null>(null);
  const [conversationId] = useState(() => `conv_${crypto.randomUUID().replaceAll("-", "")}`);
  const [models, setModels] = useState<LlmModel[]>([]);
  const [selectedModel, setSelectedModel] = useState<string>("");
  const lastQuestionRef = useRef<string | null>(null);
  const lastPendingRequestIdRef = useRef<string | null>(null);

  // Load the backend's selectable models once; default to the first.
  useEffect(() => {
    let active = true;
    getModels()
      .then((list) => {
        if (!active) return;
        setModels(list);
        setSelectedModel((current) => current || list[0]?.id || "");
      })
      .catch(() => {
        /* model picker is optional; a failure just leaves the default backend model */
      });
    return () => {
      active = false;
    };
  }, []);

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

  // Stream one pending request over SSE: render tokens live into a placeholder message, then
  // swap it for the final formatted answer + citations (or drop it and surface the error).
  const runStream = useCallback(async (requestId: string) => {
    const streamingId = crypto.randomUUID();
    setMessages((prev) => [...prev, { id: streamingId, role: "assistant", text: "", streaming: true }]);

    try {
      const status = await streamChat(requestId, {
        onDelta: (delta) =>
          setMessages((prev) =>
            prev.map((message) =>
              message.id === streamingId ? { ...message, text: message.text + delta } : message,
            ),
          ),
      });

      lastPendingRequestIdRef.current = null;

      if (status.state === "failed") {
        setMessages((prev) => prev.filter((message) => message.id !== streamingId));
        setError({
          code: status.errorCode ?? "CHATBOT_UNKNOWN_FAILURE",
          message: status.error ?? "No se pudo completar la conversacion.",
          chunksSent: status.chunksSent,
          requestId: status.requestId,
        });
        return;
      }

      const finalMessage = toAssistantMessage(status);
      setMessages((prev) =>
        prev.map((message) => (message.id === streamingId ? { ...finalMessage, id: streamingId } : message)),
      );
    } catch (error) {
      setMessages((prev) => prev.filter((message) => message.id !== streamingId));
      throw error;
    }
  }, []);

  const resumePendingRequest = useCallback(async (requestId: string) => {
    setError(null);
    setLoading(true);

    try {
      await runStream(requestId);
    } catch (error) {
      setError(toChatError(error, requestId));
    } finally {
      setLoading(false);
    }
  }, [runStream]);

  const send = useCallback(async (text: string) => {
    const trimmed = text.trim();
    if (!trimmed || loading) return;

    lastQuestionRef.current = trimmed;
    lastPendingRequestIdRef.current = null;
    setError(null);
    setMessages((prev) => [...prev, { id: crypto.randomUUID(), role: "user", text: trimmed }]);
    setLoading(true);

    try {
      const status = await startChat(trimmed, { conversationId, modelId: selectedModel || undefined });
      if (status.state === "pending") {
        lastPendingRequestIdRef.current = status.requestId;
        await runStream(status.requestId);
      } else {
        // Already settled synchronously (e.g. immediate abstention/failure): no stream to open.
        settleStatus(status);
      }
    } catch (error) {
      setError(toChatError(error, lastPendingRequestIdRef.current));
    } finally {
      setLoading(false);
    }
  }, [conversationId, loading, runStream, settleStatus, selectedModel]);

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

  const reset = useCallback(() => {
    if (loading) return;

    lastQuestionRef.current = null;
    lastPendingRequestIdRef.current = null;
    setError(null);
    setMessages([]);
  }, [loading]);

  return { messages, loading, error, send, retry, reset, models, selectedModel, setSelectedModel };
}
