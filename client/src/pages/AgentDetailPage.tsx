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
import { ServiceStatesTable } from "../components/ServiceStatesTable/ServiceStatesTable";
import { ServiceLinks as ServiceLinksBlock } from "../components/ServiceLinks/ServiceLinks";

interface DockerComposeUpPayload {
  running?: boolean;
  serviceLinks?: ServiceLinks | null;
}

interface DockerComposeUpResponse {
  ok?: boolean;
  payload?: DockerComposeUpPayload;
}

export function AgentDetailPage() {
  const { hostName: hostNameParam, tag: tagParam } = useParams();
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
  const selectedTagFromRoute = useMemo(() => {
    if (!tagParam) return "";
    try {
      return decodeURIComponent(tagParam);
    } catch {
      return tagParam;
    }
  }, [tagParam]);

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
  const [stoppingProject, setStoppingProject] = useState(false);
  const isTagRoute = selectedTagFromRoute.length > 0;

  const needsPassword = agentStatus === AGENT_STATUS_WAITING_PASSWORD;
  const isDisconnected = agentStatus === AGENT_STATUS_DISCONNECTED;
  const runningTagFromStatus = useMemo(() => {
    const runningStack = status.stacks.find(
      (s) => s.running || s.operationStatus === "in_progress"
    );
    return runningStack?.tag ?? null;
  }, [status.stacks]);
  const managedTag = selectedTagFromRoute || lastStartedTag || runningTagFromStatus;
  const managedStack = useMemo(
    () => status.stacks.find((stack) => stack.tag === managedTag) ?? null,
    [managedTag, status.stacks]
  );
  const agentTags = useMemo(
    () =>
      Array.from(
        new Set(
          status.stacks
            .map((stack) => stack.tag)
            .filter((tag) => typeof tag === "string" && tag.trim().length > 0)
        )
      ),
    [status.stacks]
  );
  const managedLinks = managedStack?.serviceLinks ?? launchLinks;
  const selectedServices = useMemo(
    () => Object.entries(managedStack?.services ?? {}),
    [managedStack]
  );
  const linkEntries = useMemo(
    () =>
      Object.entries(managedLinks ?? {}).filter(
        ([, value]) => typeof value === "string" && value.trim().length > 0
      ) as [string, string][],
    [managedLinks]
  );
  const isManagedProjectLoading =
    launching || managedStack?.operationStatus === "in_progress";
  const isManagedProjectReady =
    !!managedStack &&
    managedStack.running &&
    managedStack.operationStatus !== "in_progress";

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
      void navigate(
        `/agents/${encodeURIComponent(hostName)}/${encodeURIComponent(launchTag.trim())}`
      );
      await loadStatus();
    } catch (err) {
      setLaunchError(errMessage(err));
    } finally {
      setLaunching(false);
    }
  }

  async function onStopManagedProject() {
    if (!managedTag) return;
    setLaunchError(null);
    setLaunchOk(null);
    setStoppingProject(true);
    try {
      await api<{ ok?: boolean }>("/api/stop", {
        method: "POST",
        body: JSON.stringify({ tag: managedTag }),
      });
      setLaunchOk(`Проект ${managedTag} остановлен.`);
      setLastStartedTag(null);
      setLaunchLinks(null);
      if (isTagRoute) {
        void navigate(`/agents/${encodeURIComponent(hostName)}`);
      }
      await loadStatus();
    } catch (err) {
      setLaunchError(errMessage(err));
    } finally {
      setStoppingProject(false);
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
      <div className="agent-detail__right-controls">
        <button
          type="button"
          className="agent-detail__manage-top"
          onClick={() => void navigate(`/agents/${encodeURIComponent(hostName)}`)}
        >
          Перейти к управлению запуском сервисов
        </button>
        {agentTags.length > 0 && (
          <div className="agent-detail__tags-nav">
            {agentTags.map((tag) => (
              <button
                key={tag}
                type="button"
                className={
                  tag === managedTag
                    ? "agent-detail__tag-nav-btn agent-detail__tag-nav-btn--active"
                    : "agent-detail__tag-nav-btn"
                }
                onClick={() =>
                  void navigate(
                    `/agents/${encodeURIComponent(hostName)}/${encodeURIComponent(tag)}`
                  )
                }
              >
                {tag}
              </button>
            ))}
          </div>
        )}
      </div>

      <Link to="/agents" className="agent-detail__back">
        ← К списку
      </Link>

      {!isTagRoute && (
        <>
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
        </>
      )}

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

      {!isTagRoute &&
        !needsPassword &&
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

      {isTagRoute &&
        !needsPassword &&
        !isDisconnected &&
        agentStatus === AGENT_STATUS_PASSWORD_ACCEPTED && (
        <section className="agent-detail__tag-page">
          <h2 className="agent-detail__tag-title">{managedTag}</h2>
          <p className="agent-detail__status-line">
            Статус запуска:{" "}
            <strong>
              {managedStack?.operationStatus ??
                (isManagedProjectLoading ? "in_progress" : "нет данных")}
            </strong>
          </p>

          {selectedServices.length === 0 ? (
            <p className="agent-detail__hint">
              По тегу пока нет данных о сервисах.
            </p>
          ) : (
            <div className="agent-detail__services-center">
              <ServiceStatesTable items={selectedServices} />
            </div>
          )}

          {managedLinks ? (
            <ServiceLinksBlock links={managedLinks} />
          ) : (
            <div className="agent-detail__panel-links">
              <p className="agent-detail__panel-title">Ссылки сервисов</p>
              {linkEntries.length === 0 ? (
                <p className="agent-detail__hint">
                  Ссылки появятся после успешного запуска.
                </p>
              ) : (
                <ul className="agent-detail__links-list">
                  {linkEntries.map(([name, url]) => (
                    <li key={name}>
                      <a
                        href={url}
                        target="_blank"
                        rel="noreferrer"
                        className="agent-detail__link-item"
                      >
                        {name}: {url}
                      </a>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          )}

          <button
            type="button"
            className="agent-detail__stop-project"
            disabled={stoppingProject || isManagedProjectLoading || !isManagedProjectReady}
            onClick={() => void onStopManagedProject()}
          >
            {stoppingProject ? "Останавливаем..." : "Выключить проект"}
          </button>
        </section>
      )}
    </div>
  );
}
