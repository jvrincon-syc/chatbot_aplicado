import { useState, type ReactNode } from "react";
import { useAuth } from "./AuthContext";
import { LoginPage } from "./LoginPage";
import { RegisterPage } from "./RegisterPage";

type AuthPage = "login" | "register";

interface AuthGateProps {
  children: ReactNode;
}

export function AuthGate({ children }: AuthGateProps) {
  const { user } = useAuth();

  if (!user) {
    return <AuthRouter />;
  }

  return <>{children}</>;
}

function AuthRouter() {
  const [page, setPage] = useState<AuthPage>("login");

  return page === "login" ? (
    <LoginPage onSwitch={() => setPage("register")} />
  ) : (
    <RegisterPage onSwitch={() => setPage("login")} />
  );
}
