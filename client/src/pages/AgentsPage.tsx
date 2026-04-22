import { useMemo } from "react";
import { Link } from "react-router-dom";
import {
  AGENT_STATUS_DISCONNECTED,
  AGENT_STATUS_PASSWORD_ACCEPTED,
  AGENT_STATUS_WAITING_PASSWORD,
} from "../constants";
import { useAppStore } from "../store/appStore";
import "../components/StackCard/StackCard.css";
import "./AgentsPage.css";

export function AgentsPage() {
  const { status } = useAppStore();

  const entries = useMemo(() => {
    return Object.entries(status.agents).sort(([a], [b]) =>
      a.localeCompare(b, undefined, { sensitivity: "base" })
    );
  }, [status.agents]);

  return (
    <div className="agents-page">
      <h1 className="agents-page__title">Агенты</h1>
      <div className="agents-page__list">
        {entries.length === 0 ? (
          <p className="agents-page__empty">Подключённых агентов нет.</p>
        ) : (
          entries.map(([hostName, agentStatus]) => {
            const isWaiting =
              agentStatus === AGENT_STATUS_WAITING_PASSWORD;
            const isPasswordAccepted =
              agentStatus === AGENT_STATUS_PASSWORD_ACCEPTED;
            const isDisconnected =
              agentStatus === AGENT_STATUS_DISCONNECTED;
            const cardClass = isDisconnected
              ? "stack-card is-disconnected agents-page__card--click"
              : isWaiting
                ? "stack-card is-loading agents-page__card--click"
                : isPasswordAccepted
                  ? "stack-card is-running agents-page__card--click"
                  : "stack-card agents-page__card--click";
            const to = `/agents/${encodeURIComponent(hostName)}`;
            return (
              <div key={hostName} className="agents-page__card-wrap">
                <Link to={to} className="agents-page__card-link">
                  <div className={cardClass}>
                    <span className="stack-card__tag">{hostName}</span>
                    <span className="stack-card__state">{agentStatus}</span>
                  </div>
                </Link>
              </div>
            );
          })
        )}
      </div>
    </div>
  );
}
