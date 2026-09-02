import { ApiError, getJson, postJson } from "./client";
import type { Citation, ChatRequestStatus } from "../types";

export interface StartChatOptions {
  conversationId?: string;
  messageId?: string;
  topK?: number;
  modelId?: string;
}

export const startChat = (question: string, options: StartChatOptions = {}) =>
  postJson<ChatRequestStatus>("/api/chat/requests", {
    question,
    conversationId: options.conversationId,
    messageId: options.messageId,
    topK: options.topK,
    modelId: options.modelId,
  });

/** A selectable LLM model exposed by the backend for the model picker. */
export interface LlmModel {
  id: string;
  label: string;
}

export const getModels = () => getJson<LlmModel[]>("/api/llm/models");

export const getChatRequest = (requestId: string) =>
  getJson<ChatRequestStatus>(`/api/chat/requests/${encodeURIComponent(requestId)}`);

export interface ChatStreamHandlers {
  /** Called for each answer token as it is generated, so the UI can render it live. */
  onDelta: (delta: string) => void;
}

/**
 * Subscribe to a request's Server-Sent Events stream and resolve with the terminal status.
 * Replaces client polling: tokens arrive as the model produces them (TTFT ~= prefill), and the
 * backend replays from the start of the stream so opening late never drops earlier tokens.
 */
export function streamChat(requestId: string, handlers: ChatStreamHandlers): Promise<ChatRequestStatus> {
  return new Promise((resolve, reject) => {
    const source = new EventSource(`/api/chat/requests/${encodeURIComponent(requestId)}/events`);
    let settled = false;

    const finish = (status: ChatRequestStatus) => {
      if (settled) return;
      settled = true;
      source.close();
      resolve(status);
    };

    source.addEventListener("chat.answer.delta.v1", (event) => {
      try {
        const data = JSON.parse((event as MessageEvent).data) as { delta?: string };
        if (data.delta) handlers.onDelta(data.delta);
      } catch {
        // Ignore a malformed frame; the terminal event carries the authoritative answer.
      }
    });

    source.addEventListener("chat.answer.completed.v1", (event) => {
      const data = JSON.parse((event as MessageEvent).data) as {
        answer?: string;
        abstained?: boolean;
        citations?: Citation[];
      };
      finish({
        requestId,
        question: "",
        state: "completed",
        answer: data.answer ?? "",
        abstained: data.abstained ?? false,
        citations: data.citations ?? [],
        chunks: [],
      });
    });

    source.addEventListener("chat.request.failed.v1", (event) => {
      const data = JSON.parse((event as MessageEvent).data) as { errorCode?: string; error?: string };
      finish({
        requestId,
        question: "",
        state: "failed",
        errorCode: data.errorCode ?? "CHATBOT_UNKNOWN_FAILURE",
        error: data.error ?? "No se pudo completar la conversacion.",
      });
    });

    source.onerror = () => {
      if (settled) return;
      settled = true;
      source.close();
      reject(new ApiError(
        0,
        "Se perdio la conexion con el servidor mientras se generaba la respuesta.",
        "CHATBOT_STREAM_ERROR",
      ));
    };
  });
}
