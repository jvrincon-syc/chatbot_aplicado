import { AuthProvider } from "./auth/AuthContext";
import { AuthGate } from "./auth/AuthGate";
import { ChatPage } from "./features/chat/ChatPage";

export default function App() {
  return (
    <AuthProvider>
      <AuthGate>
        <ChatPage />
      </AuthGate>
    </AuthProvider>
  );
}
