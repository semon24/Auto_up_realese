import { BrowserRouter, NavLink, Navigate, Route, Routes } from "react-router-dom";
import { SHOW_PROJECTS_UI } from "./constants";
import { useRuntimePolling } from "./hooks/useRuntimePolling";
import { PollingProvider } from "./pollingContext";
import { AgentDetailPage } from "./pages/AgentDetailPage";
import { AgentsPage } from "./pages/AgentsPage";
import { MainPage } from "./pages/MainPage";
import "./App.css";

function AppShell() {
  const polling = useRuntimePolling();

  if (!SHOW_PROJECTS_UI) {
    return (
      <PollingProvider value={polling}>
        <Routes>
          <Route path="/agents" element={<AgentsPage />} />
          <Route path="/agents/:hostName" element={<AgentDetailPage />} />
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
          </Routes>
        </div>
      </div>
    </PollingProvider>
  );
}

export default function App() {
  return (
    <BrowserRouter>
      <AppShell />
    </BrowserRouter>
  );
}
