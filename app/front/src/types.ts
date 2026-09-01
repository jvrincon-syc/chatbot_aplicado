// Shared domain types, mirrored from the backend contract.

export interface Citation {
  documentId: string;
  documentTitle?: string | null;
  page?: string | null;
  section?: string | null;
  sourceUrl?: string | null;
}

export interface ChatRequestChunk {
  documentId: string;
  metadata?: Record<string, string | null> | null;
}

export interface ChatResponse {
  answer: string;
  citations: Citation[];
  abstained: boolean;
}

export type ChatRequestState = "pending" | "completed" | "failed";

export interface ChatRequestStatus {
  requestId: string;
  question: string;
  state: ChatRequestState;
  conversationId?: string | null;
  dispatchId?: string | null;
  chunksSent?: number | null;
  chunks?: ChatRequestChunk[] | null;
  answer?: string | null;
  citations?: Citation[] | null;
  abstained?: boolean | null;
  errorCode?: string | null;
  error?: string | null;
}

// Structured chat failure, preserved from the failed request status (or a transport-level
// ApiError) so the UI can distinguish error kinds instead of collapsing everything to a string.
export interface ChatError {
  code: string;
  message: string;
  chunksSent?: number | null;
  requestId?: string | null;
}

export type Role = "user" | "assistant";

export interface ChatMessage {
  id: string;
  role: Role;
  text: string;
  abstained?: boolean;
  citations?: Citation[];
  // True while tokens are still streaming in over SSE; cleared once the terminal event lands.
  streaming?: boolean;
}
