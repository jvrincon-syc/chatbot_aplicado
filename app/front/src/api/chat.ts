import { getJson, postJson } from "./client";
import type { ChatRequestStatus } from "../types";

export interface StartChatOptions {
  conversationId?: string;
  messageId?: string;
  topK?: number;
}

export const startChat = (question: string, options: StartChatOptions = {}) =>
  postJson<ChatRequestStatus>("/api/chat/requests", {
    question,
    conversationId: options.conversationId,
    messageId: options.messageId,
    topK: options.topK,
  });

export const getChatRequest = (requestId: string) =>
  getJson<ChatRequestStatus>(`/api/chat/requests/${encodeURIComponent(requestId)}`);
