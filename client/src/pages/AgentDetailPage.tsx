import { useMemo, useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api, errMessage } from "../apiClient";
import "./AgentDetailPage.css";
import {
  AGENT_STATUS_DISCONNECTED,
  AGENT_STATUS_PASSWORD_ACCEPTED,
  AGENT_STATUS_WAITING_PASSWORD,
} from "../constants";
import { usePolling } from "../pollingContext";
import { useAppStore } from "../store/appStore";
import "../components/StackCard/StackCard.css";
import type { ServiceLinks } from "../types";

interface DockerComposeUpPayload {
  running?: boolean;
  serviceLinks?: ServiceLinks | null;
}

interface DockerComposeUpResponse {
  ok?: boolean;
  payload?: DockerComposeUpPayload;
}

export function AgentDetailPage() {
  const { hostName: hostNameParam } = useParams();
  const navigate = useNavigate();
  const { status, items, tagsLoading } = useAppStore();
  const { loadStatus, loadTags } = usePolling();

  const hostName = useMemo(() => {
    if (!hostNameParam) return "";
    try {
      return decodeURIComponent(hostNameParam);
    } catch {
      return hostNameParam;
    }
  }, [hostNameParam]);

  const agentStatus = status.agents[hostName];
  const [password, setPassword] = useState("");
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [launchTag, setLaunchTag] = useState("");
  const [launching, setLaunching] = useState(false);
  const [launchError, setLaunchError] = useState<string | null>(null);
  const [launchOk, setLaunchOk] = useState<string | null>(null);
  const [launchLinks, setLaunchLinks] = useState<ServiceLinks | null>(null);
  const [lastStartedTag, setLastStartedTag] = useState<string | null>(null);

  const needsPassword = agentStatus === AGENT_STATUS_WAITING_PASSWORD;
  const isDisconnected = agentStatus === AGENT_STATUS_DISCONNECTED;
  const runningTagFromStatus = useMemo(() => {
    const runningStack = status.stacks.find(
      (s) => s.running || s.operationStatus === "in_progress"
    );
    return runningStack?.tag ?? null;
  }, [status.stacks]);
  const activeTagLabel = launchError
    ? null
    : (runningTagFromStatus ?? lastStartedTag);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setSubmitError(null);
    if (!password.trim()) {
      setSubmitError("Введите пароль");
      return;
    }
    setSubmitting(true);
    try {
      await api<{ ok?: boolean }>(
        `/api/agents/${encodeURIComponent(hostName)}/password`,
        {
          method: "POST",
          body: JSON.stringify({ password: password.trim() }),
        }
      );
      setPassword("");
      await loadStatus();
    } catch (err) {
      setSubmitError(errMessage(err));
    } finally {
      setSubmitting(false);
    }
  }

  async function onDeleteDisconnected() {
    setDeleteError(null);
    setDeleting(true);
    try {
      await api<{ ok?: boolean }>(
        `/api/agents/${encodeURIComponent(hostName)}`,
        { method: "DELETE" }
      );
      await loadStatus();
      void navigate("/agents");
    } catch (err) {
      setDeleteError(errMessage(err));
    } finally {
      setDeleting(false);
    }
  }

  async function onLaunch(e: FormEvent) {
    e.preventDefault();
    setLaunchError(null);
    setLaunchOk(null);
    setLaunchLinks(null);
    if (!launchTag.trim()) {
      setLaunchError("Введите тег для запуска");
      return;
    }
    setLaunching(true);
    try {
      const res = await api<DockerComposeUpResponse>(
        `/api/agents/${encodeURIComponent(hostName)}/docker-compose-up`,
        {
          method: "POST",
          body: JSON.stringify({
            tag: launchTag.trim(),
          }),
        }
      );
      setLaunchOk("Команда запуска отправлена агенту.");
      setLaunchLinks(res.payload?.serviceLinks ?? null);
      setLastStartedTag(launchTag.trim());
      await loadStatus();
    } catch (err) {
      setLaunchError(errMessage(err));
    } finally {
      setLaunching(false);
    }
  }

  if (!hostName) {
    return (
      <div className="agent-detail">
        <p className="agent-detail__error">Не указан хост.</p>
        <Link to="/agents" className="agent-detail__back">
          ← К списку
        </Link>
      </div>
    );
  }

  if (agentStatus === undefined) {
    return (
      <div className="agent-detail">
        <p className="agent-detail__hint">
          Агент «{hostName}» не в списке (или ещё не подключился). Обновите
          статус или вернитесь назад.
        </p>
        <Link to="/agents" className="agent-detail__back">
          ← К списку
        </Link>
      </div>
    );
  }

  return (
    <div className="agent-detail">
      <Link to="/agents" className="agent-detail__back">
        ← К списку
      </Link>

      <h1 className="agent-detail__title">{hostName}</h1>
      <p
        className={
          isDisconnected
            ? "agent-detail__status-line agent-detail__status-line--disconnected"
            : agentStatus === AGENT_STATUS_PASSWORD_ACCEPTED
              ? "agent-detail__status-line agent-detail__status-line--accepted"
              : "agent-detail__status-line"
        }
      >
        Статус: <strong>{agentStatus}</strong>
      </p>

      {isDisconnected && (
        <p className="agent-detail__hint agent-detail__hint--disconnected">
          Сокет закрыт после принятого пароля. После переподключения агента
          статус снова станет «password accepted» без повторного ввода пароля.
        </p>
      )}

      {isDisconnected && (
        <div className="agent-detail__delete-wrap">
          {deleteError && (
            <p className="agent-detail__error">{deleteError}</p>
          )}
          <button
            type="button"
            className="agent-detail__delete"
            disabled={deleting}
            onClick={() => void onDeleteDisconnected()}
          >
            {deleting ? "…" : "Удалить из списка"}
          </button>
          <p className="agent-detail__hint agent-detail__delete-hint">
            Запись исчезнет из JSON — как если бы агент никогда не подключался.
          </p>
        </div>
      )}

      {needsPassword && (
        <form className="agent-detail__form" onSubmit={(e) => void onSubmit(e)}>
          <label className="agent-detail__label" htmlFor="agent-pw">
            Пароль агента
          </label>
          <input
            id="agent-pw"
            type="password"
            className="agent-detail__input"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            placeholder="Вставьте пароль из agent-credentials"
            autoComplete="off"
          />
          {submitError && (
            <p className="agent-detail__error">{submitError}</p>
          )}
          <button
            type="submit"
            className="agent-detail__submit"
            disabled={submitting}
          >
            {submitting ? "…" : "Подтвердить"}
          </button>
        </form>
      )}

      {!needsPassword &&
        !isDisconnected &&
        agentStatus === AGENT_STATUS_PASSWORD_ACCEPTED && (
          <>
            <p className="agent-detail__ok">Пароль принят.</p>
            <div className="agent-detail__launch-layout">
              <form
                className="agent-detail__form agent-detail__form--launch"
                onSubmit={(e) => void onLaunch(e)}
              >
                <label className="agent-detail__label" htmlFor="agent-launch-tag">
                  Тег образа
                </label>
                <input
                  id="agent-launch-tag"
                  list="agent-launch-tags"
                  type="text"
                  className="agent-detail__input"
                  value={launchTag}
                  onChange={(e) => setLaunchTag(e.target.value)}
                  placeholder="Введите или выберите тег"
                  autoComplete="off"
                />
                <datalist id="agent-launch-tags">
                  {items.map((item) => (
                    <option key={item.tag} value={item.tag} />
                  ))}
                </datalist>
                <button
                  type="button"
                  className="agent-detail__refresh"
                  disabled={tagsLoading || launching}
                  onClick={() => void loadTags()}
                >
                  {tagsLoading ? "…" : "Обновить теги"}
                </button>
                <button
                  type="submit"
                  className="agent-detail__submit"
                  disabled={launching}
                >
                  {launching ? "…" : "Поднять проект"}
                </button>
              </form>
              {activeTagLabel && (
                <button type="button" className="agent-detail__active-tag-btn">
                  {activeTagLabel}
                </button>
              )}
            </div>
            <div className="agent-detail__launch-feedback">
              {launchError && <p className="agent-detail__error">{launchError}</p>}
              {launchOk && <p className="agent-detail__ok">{launchOk}</p>}
              {launchLinks && (
                <pre className="agent-detail__links">
                  {JSON.stringify(launchLinks, null, 2)}
                </pre>
              )}
            </div>
          </>
        )}
    </div>
  );
}
