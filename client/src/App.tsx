import { FormEvent, useEffect, useState } from "react";
import { BrowserRouter, NavLink, Navigate, Route, Routes } from "react-router-dom";
import { errMessage, getAuthState, login } from "./apiClient";
import { SHOW_PROJECTS_UI } from "./constants";
import { useRuntimePolling } from "./hooks/useRuntimePolling";
import { PollingProvider } from "./pollingContext";
import { AgentDetailPage } from "./pages/AgentDetailPage";
import { AgentsPage } from "./pages/AgentsPage";
import { MainPage } from "./pages/MainPage";
import "./App.css";

type AuthStatus = "checking" | "signed-out" | "signed-in";

function LoginPage({ onSignedIn }: { onSignedIn: () => void }) {
  const [userName, setUserName] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);
    setSubmitting(true);

    try {
      await login({ userName, password });
      onSignedIn();
    } catch (e) {
      setError(errMessage(e));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <main className="login-page">
      <form className="login-panel" onSubmit={handleSubmit}>
        <div className="login-panel__head">
          <span className="login-panel__eyebrow">Auto Up Release</span>
          <h1>Sign in</h1>
        </div>
        <label className="login-field">
          <span>Login</span>
          <input
            autoComplete="username"
            autoFocus
            value={userName}
            onChange={(event) => setUserName(event.target.value)}
          />
        </label>
        <label className="login-field">
          <span>Password</span>
          <input
            autoComplete="current-password"
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </label>
        {error && <div className="login-panel__error">{error}</div>}
        <button className="login-panel__button" disabled={submitting} type="submit">
          {submitting ? "Signing in..." : "Sign in"}
        </button>
      </form>
    </main>
  );
}

function AppShell() {
  const polling = useRuntimePolling();

  if (!SHOW_PROJECTS_UI) {
    return (
      <PollingProvider value={polling}>
        <Routes>
          <Route path="/agents" element={<AgentsPage />} />
          <Route path="/agents/:hostName" element={<AgentDetailPage />} />
          <Route path="/agents/:hostName/:tag" element={<AgentDetailPage />} />
          <Route path="/" element={<Navigate to="/agents" replace />} />
          <Route path="*" element={<Navigate to="/agents" replace />} />
        </Routes>
      </PollingProvider>
    );
  }

  return (
    <PollingProvider value={polling}>
      <div className="app-shell">
        <nav className="app-nav" aria-label="Основное меню">
          <NavLink
            to="/agents"
            className={({ isActive }) => (isActive ? "active" : undefined)}
          >
            Агенты
          </NavLink>
          <NavLink
            to="/"
            end
            className={({ isActive }) => (isActive ? "active" : undefined)}
          >
            Проекты
          </NavLink>
        </nav>
        <div className="app-shell__main">
          <Routes>
            <Route path="/" element={<MainPage />} />
            <Route path="/agents" element={<AgentsPage />} />
            <Route path="/agents/:hostName" element={<AgentDetailPage />} />
            <Route path="/agents/:hostName/:tag" element={<AgentDetailPage />} />
          </Routes>
        </div>
      </div>
    </PollingProvider>
  );
}

export default function App() {
  const [authStatus, setAuthStatus] = useState<AuthStatus>("checking");

  useEffect(() => {
    let alive = true;

    getAuthState()
      .then((state) => {
        if (alive) setAuthStatus(state.authenticated ? "signed-in" : "signed-out");
      })
      .catch(() => {
        if (alive) setAuthStatus("signed-out");
      });

    return () => {
      alive = false;
    };
  }, []);

  if (authStatus === "checking") {
    return <div className="auth-loading">Loading...</div>;
  }

  if (authStatus === "signed-out") {
    return <LoginPage onSignedIn={() => setAuthStatus("signed-in")} />;
  }

  return (
    <BrowserRouter>
      <AppShell />
    </BrowserRouter>
  );
}
