import { postJson } from "./client";
import type { ChatResponse, EvidenceFragment } from "../types";

// Sends the question plus its evidence fragments (empty for now — the backend abstains fail-closed
// until server-side retrieval feeds them).
export const sendChat = (message: string, fragments: EvidenceFragment[] = []) =>
  postJson<ChatResponse>("/api/chat", { message, fragments });
