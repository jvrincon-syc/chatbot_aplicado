import { createContext, useContext, useState, useCallback, type ReactNode } from "react";

interface User {
  name: string;
  email: string;
}

interface AuthState {
  user: User | null;
  loading: boolean;
  error: string | null;
}

interface AuthCtx extends AuthState {
  login: (email: string, password: string) => Promise<void>;
  register: (name: string, email: string, password: string) => Promise<void>;
  logout: () => void;
  clearError: () => void;
}

const AuthContext = createContext<AuthCtx | null>(null);

const STORAGE_KEY = "sst_chatbot_user";

function loadUser(): User | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ user: loadUser(), loading: false, error: null });

  const login = useCallback(async (email: string, _password: string) => {
    setState((s) => ({ ...s, loading: true, error: null }));
    // Simulated delay — replace with real POST /api/auth/login
    await new Promise((r) => setTimeout(r, 600));

    if (!email) {
      setState((s) => ({ ...s, loading: false, error: "Ingresa tu correo electrónico." }));
      return;
    }

    const user: User = { name: email.split("@")[0], email };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(user));
    setState({ user, loading: false, error: null });
  }, []);

  const register = useCallback(async (name: string, email: string, _password: string) => {
    setState((s) => ({ ...s, loading: true, error: null }));
    // Simulated delay — replace with real POST /api/auth/register
    await new Promise((r) => setTimeout(r, 600));

    if (!name || !email) {
      setState((s) => ({ ...s, loading: false, error: "Completa todos los campos." }));
      return;
    }

    const user: User = { name, email };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(user));
    setState({ user, loading: false, error: null });
  }, []);

  const logout = useCallback(() => {
    localStorage.removeItem(STORAGE_KEY);
    setState({ user: null, loading: false, error: null });
  }, []);

  const clearError = useCallback(() => {
    setState((s) => ({ ...s, error: null }));
  }, []);

  return (
    <AuthContext.Provider value={{ ...state, login, register, logout, clearError }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthCtx {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
