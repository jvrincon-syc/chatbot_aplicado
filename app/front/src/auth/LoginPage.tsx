import { useState } from "react";
import { useAuth } from "./AuthContext";

const ICON_SHIELD = '<svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"/><path d="m9 12 2 2 4-4"/></svg>';
const ICON_EYE = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0"/><circle cx="12" cy="12" r="3"/></svg>';
const ICON_EYE_OFF = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49"/><path d="M14.084 14.158a3 3 0 0 1-4.242-4.242"/><path d="M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143"/><path d="m2 2 20 20"/></svg>';

interface LoginPageProps {
  onSwitch: () => void;
}

export function LoginPage({ onSwitch }: LoginPageProps) {
  const { login, loading, error, clearError } = useAuth();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPw, setShowPw] = useState(false);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    login(email, password);
  };

  return (
    <div className="auth">
      <div className="auth__card">
        <div className="auth__header">
          <div className="auth__icon" dangerouslySetInnerHTML={{ __html: ICON_SHIELD }} />
          <h1 className="auth__title">Iniciar sesión</h1>
          <p className="auth__subtitle">Accede al asistente SST</p>
        </div>

        {error && (
          <div className="auth__error" role="alert">
            {error}
          </div>
        )}

        <form className="auth__form" onSubmit={handleSubmit}>
          <div className="auth__field">
            <label className="auth__label" htmlFor="login-email">Correo electrónico</label>
            <input
              id="login-email"
              className="auth__input"
              type="email"
              value={email}
              onChange={(e) => { setEmail(e.target.value); clearError(); }}
              placeholder="tu@empresa.com"
              autoComplete="email"
              required
            />
          </div>

          <div className="auth__field">
            <label className="auth__label" htmlFor="login-password">Contraseña</label>
            <div className="auth__input-wrap">
              <input
                id="login-password"
                className="auth__input auth__input--pw"
                type={showPw ? "text" : "password"}
                value={password}
                onChange={(e) => { setPassword(e.target.value); clearError(); }}
                placeholder="••••••••"
                autoComplete="current-password"
                required
              />
              <button
                type="button"
                className="auth__pw-toggle"
                onClick={() => setShowPw(!showPw)}
                aria-label={showPw ? "Ocultar contraseña" : "Mostrar contraseña"
                }
                dangerouslySetInnerHTML={{ __html: showPw ? ICON_EYE_OFF : ICON_EYE }}
              />
            </div>
          </div>

          <button type="submit" className="auth__btn" disabled={loading}>
            {loading ? "Ingresando..." : "Iniciar sesión"}
          </button>
        </form>

        <p className="auth__switch">
          ¿No tienes cuenta?{" "}
          <button type="button" className="auth__link" onClick={onSwitch}>
            Regístrate aquí
          </button>
        </p>
      </div>
    </div>
  );
}
