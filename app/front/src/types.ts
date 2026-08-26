// Shared domain types, mirrored from the backend contract (Chatbot.Sst.Domain).

export interface Citation {
  documentId: string;
  documentTitle?: string | null;
  page?: string | null;
  section?: string | null;
}

export interface ChatResponse {
  answer: string;
  citations: Citation[];
  abstained: boolean;
}

export interface EvidenceFragment {
  content: string;
  documentId?: string;
  documentTitle?: string;
  page?: string;
  section?: string;
  score?: number;
}

export type Role = "user" | "assistant";

export interface ChatMessage {
  id: string;
  role: Role;
  text: string;
  abstained?: boolean;
  citations?: Citation[];
}
