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
import type { ServiceLinks, SslCertificateInfo } from "../types";
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

interface ManagedProjectOverride {
  tag: string;
  running: boolean;
  operationStatus: string;
}

interface AddDomainsResponse {
  ok?: boolean;
  payload?: {
    serviceDomains?: string[] | null;
  };
}

interface DeleteDomainResponse {
  ok?: boolean;
  payload?: {
    serviceDomain?: string | null;
  };
}

function isPendingOperationStatus(status?: string | null) {
  return (
    status === "in_progress" ||
    status === "deleting" ||
    status === "stopping" ||
    status === "starting"
  );
}

function isReadonlyAgent(type?: string | null) {
  return type?.trim().toLowerCase() === "readonly";
}

export function AgentDetailPage() {
  const { hostName: hostNameParam, tag: tagParam } = useParams();
  const navigate = useNavigate();

  const hostName = useMemo(() => {
    if (!hostNameParam) return "";
    try {
      return decodeURIComponent(hostNameParam);
    } catch {
      return hostNameParam;
    }
  }, [hostNameParam]);

  const {
    status,
    tagsByHost,
    tagsLoadingHost,
    tagsErrorByHost,
  } = useAppStore();

  const tagItemsForHost = tagsByHost[hostName] ?? [];
  const tagsLoading = tagsLoadingHost === hostName;
  const tagsError = tagsErrorByHost[hostName] ?? null;
  const { loadStatus, loadTags } = usePolling();
  const selectedTagFromRoute = useMemo(() => {
    if (!tagParam) return "";
    try {
      return decodeURIComponent(tagParam);
    } catch {
      return tagParam;
    }
  }, [tagParam]);

  const agentInfo = status.agents[hostName];
  const agentStatus = agentInfo?.status;
  const readonlyAgent = isReadonlyAgent(agentInfo?.type);
  const agentDisconnectedAtText = useMemo(() => {
    const raw = agentInfo?.disconnectedAtUtc?.trim();
    if (!raw) return null;

    const parsed = new Date(raw);
    if (Number.isNaN(parsed.getTime())) return raw;

    return parsed.toLocaleString("ru-RU");
  }, [agentInfo?.disconnectedAtUtc]);
  const agentStacks = status.stacksByHost[hostName] ?? [];
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
  const [isLaunchPanelOpen, setIsLaunchPanelOpen] = useState(false);
  const [isDomainsPanelOpen, setIsDomainsPanelOpen] = useState(false);
  const [domainsInput, setDomainsInput] = useState("");
  const [domainsSubmitting, setDomainsSubmitting] = useState(false);
  const [removingDomain, setRemovingDomain] = useState<string | null>(null);
  const [domainsError, setDomainsError] = useState<string | null>(null);
  const [domainsOk, setDomainsOk] = useState<string | null>(null);
  const [lastStartedTag, setLastStartedTag] = useState<string | null>(null);
  const [managedProjectOverride, setManagedProjectOverride] = useState<ManagedProjectOverride | null>(null);
  const [pausingProject, setPausingProject] = useState(false);
  const [stoppingProject, setStoppingProject] = useState(false);
  const [restartingProject, setRestartingProject] = useState(false);
  const isTagRoute = selectedTagFromRoute.length > 0;

  const needsPassword = agentStatus === AGENT_STATUS_WAITING_PASSWORD;
  const isDisconnected = agentStatus === AGENT_STATUS_DISCONNECTED;
  const isManagementLocked = needsPassword || isDisconnected;
  const runningTagFromStatus = useMemo(() => {
    const runningStack = agentStacks.find(
      (s) => s.running || isPendingOperationStatus(s.operationStatus)
    );
    return runningStack?.tag ?? null;
  }, [agentStacks]);
  const managedTag = selectedTagFromRoute || lastStartedTag || runningTagFromStatus;
  const managedStack = useMemo(
    () => agentStacks.find((stack) => stack.tag === managedTag) ?? null,
    [managedTag, agentStacks]
  );
  const effectiveManagedRunning =
    managedProjectOverride?.tag === managedTag
      ? managedProjectOverride.running
      : !!managedStack?.running;
  const effectiveManagedOperationStatus =
    managedProjectOverride?.tag === managedTag
      ? managedProjectOverride.operationStatus
      : managedStack?.operationStatus ?? null;
  const agentTags = useMemo(
    () =>
      Array.from(
        new Set(
          agentStacks
            .map((stack) => stack.tag)
            .filter((tag) => typeof tag === "string" && tag.trim().length > 0)
        )
      ),
    [agentStacks]
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
  const managedCertificates = useMemo(
    () => managedStack?.certificates ?? [],
    [managedStack]
  );
  const isManagedProjectLoading =
    launching || isPendingOperationStatus(effectiveManagedOperationStatus);
  const hasManagedProject = !!managedStack;
  const isManagedProjectRunning = effectiveManagedRunning;
  const isManagedProjectReady =
    hasManagedProject &&
    effectiveManagedRunning &&
    !isPendingOperationStatus(effectiveManagedOperationStatus);
  const managedOperationStatus =
    effectiveManagedOperationStatus ??
    (isManagedProjectLoading ? "in_progress" : "нет данных");
  const managedOperationStatusClassName =
    managedOperationStatus === "success"
      ? "agent-detail__status-line agent-detail__status-line--success"
      : isPendingOperationStatus(managedOperationStatus)
        ? "agent-detail__status-line agent-detail__status-line--progress"
        : "agent-detail__status-line";
  const shouldShowManagedLinks = isManagedProjectReady && linkEntries.length > 0;
  const isProjectActionPending =
    pausingProject || stoppingProject || restartingProject;
  const shouldRenderPauseAction =
    isManagedProjectRunning || isPendingOperationStatus(effectiveManagedOperationStatus);
  const canRestartManagedProject =
    hasManagedProject &&
    isManagedProjectRunning &&
    !isManagedProjectLoading &&
    !isProjectActionPending &&
    !readonlyAgent;
  const canPauseManagedProject =
    hasManagedProject &&
    isManagedProjectRunning &&
    !isManagedProjectLoading &&
    !isProjectActionPending &&
    !readonlyAgent;
  const canStartManagedProject =
    hasManagedProject &&
    !isManagedProjectRunning &&
    !isManagedProjectLoading &&
    !isProjectActionPending &&
    !readonlyAgent;
  const canDeleteManagedProject =
    hasManagedProject && !isManagedProjectLoading && !isProjectActionPending && !readonlyAgent;

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
    if (readonlyAgent) return;
    setLaunchError(null);
    setLaunchOk(null);
    setLaunchLinks(null);
    if (!launchTag.trim()) {
    setLaunchError("Введите тег для запуска");
      return;
    }
    setManagedProjectOverride(null);
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
    if (readonlyAgent) return;
    setLaunchError(null);
    setLaunchOk(null);
    setManagedProjectOverride(null);
    setStoppingProject(true);
    try {
      await api<{ ok?: boolean }>(
        `/api/agents/${encodeURIComponent(hostName)}/docker-compose-down`,
        {
          method: "POST",
          body: JSON.stringify({ tag: managedTag }),
        }
      );
      setLaunchOk(`Проект ${managedTag} выключен.`);
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

  async function onPauseManagedProject() {
    if (!managedTag) return;
    if (readonlyAgent) return;
    setLaunchError(null);
    setLaunchOk(null);
    setManagedProjectOverride(null);
    setPausingProject(true);
    try {
      await api<{ ok?: boolean }>(
        `/api/agents/${encodeURIComponent(hostName)}/docker-compose-stop`,
        {
          method: "POST",
          body: JSON.stringify({ tag: managedTag }),
        }
      );
      setLaunchOk(`Проект ${managedTag} остановлен.`);
      setManagedProjectOverride({
        tag: managedTag,
        running: false,
        operationStatus: "success",
      });
      await loadStatus();
    } catch (err) {
      setLaunchError(errMessage(err));
    } finally {
      setPausingProject(false);
    }
  }

  async function onStartManagedProject() {
    if (!managedTag) return;
    if (readonlyAgent) return;
    setLaunchError(null);
    setLaunchOk(null);
    setManagedProjectOverride(null);
    setPausingProject(true);
    try {
      const res = await api<DockerComposeUpResponse>(
        `/api/agents/${encodeURIComponent(hostName)}/docker-compose-up`,
        {
          method: "POST",
          body: JSON.stringify({ tag: managedTag }),
        }
      );
      setLaunchOk(`Проект ${managedTag} поднимается.`);
      setLaunchLinks(res.payload?.serviceLinks ?? null);
      setManagedProjectOverride({
        tag: managedTag,
        running: true,
        operationStatus: "in_progress",
      });
      await loadStatus();
    } catch (err) {
      setLaunchError(errMessage(err));
    } finally {
      setPausingProject(false);
    }
  }

  async function onRestartManagedProject() {
    if (!managedTag) return;
    if (readonlyAgent) return;
    setLaunchError(null);
    setLaunchOk(null);
    setManagedProjectOverride(null);
    setRestartingProject(true);
    try {
      await api<{ ok?: boolean }>(
        `/api/agents/${encodeURIComponent(hostName)}/docker-compose-restart`,
        {
          method: "POST",
          body: JSON.stringify({ tag: managedTag }),
        }
      );
      setLaunchOk(`Проект ${managedTag} перезапущен.`);
      await loadStatus();
    } catch (err) {
      setLaunchError(errMessage(err));
    } finally {
      setRestartingProject(false);
    }
  }

  async function onAddDomains(e: FormEvent) {
    e.preventDefault();
    if (!managedTag || readonlyAgent) return;

    setDomainsError(null);
    setDomainsOk(null);

    const domains = domainsInput
      .split(",")
      .map((item) => item.trim())
      .filter((item) => item.length > 0);

    if (domains.length === 0) {
      setDomainsError("Введите хотя бы один домен через запятую");
      return;
    }

    setDomainsSubmitting(true);
    try {
      await api<AddDomainsResponse>(
        `/api/agents/${encodeURIComponent(hostName)}/add-domains`,
        {
          method: "POST",
          body: JSON.stringify({
            tag: managedTag,
            domains,
          }),
        }
      );
      setDomainsOk("Домены привязаны.");
      setDomainsInput("");
      await loadStatus();
    } catch (err) {
      setDomainsError(errMessage(err));
    } finally {
      setDomainsSubmitting(false);
    }
  }

  async function onDeleteDomain(domain: string) {
    if (!managedTag || readonlyAgent) return;

    setDomainsError(null);
    setDomainsOk(null);
    setRemovingDomain(domain);

    try {
      await api<DeleteDomainResponse>(
        `/api/agents/${encodeURIComponent(hostName)}/delete-domain`,
        {
          method: "POST",
          body: JSON.stringify({
            tag: managedTag,
            domain,
          }),
        }
      );
      setDomainsOk(`Домен ${domain} удалён.`);
      await loadStatus();
    } catch (err) {
      setDomainsError(errMessage(err));
    } finally {
      setRemovingDomain(null);
    }
  }

  function formatCertificateDate(value?: string | null) {
    if (!value) return "—";
    const parsed = new Date(value);
    if (Number.isNaN(parsed.getTime())) return value;
    return parsed.toLocaleString("ru-RU");
  }

  function certificateStatusClassName(certificate: SslCertificateInfo) {
    if (certificate.error) return "agent-detail__certificate agent-detail__certificate--error";
    if ((certificate.daysLeft ?? -1) < 7) return "agent-detail__certificate agent-detail__certificate--danger";
    if ((certificate.daysLeft ?? -1) < 30) return "agent-detail__certificate agent-detail__certificate--warn";
    return "agent-detail__certificate agent-detail__certificate--ok";
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
          disabled={isManagementLocked}
          onClick={() => void navigate(`/agents/${encodeURIComponent(hostName)}`)}
        >
          Перейти к управлению запуском сервисов
        </button>
        {!isTagRoute && (
          <div className="agent-detail__summary-card">
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
            {readonlyAgent && (
              <p className="agent-detail__meta-line">
                Режим: <strong>readonly</strong>
              </p>
            )}
          </div>
        )}
      </div>

      <Link to="/agents" className="agent-detail__back">
        ← К списку
      </Link>

      {!isTagRoute && agentDisconnectedAtText && (
        <p className="agent-detail__meta-line">
          Дата дисконнекта: <strong>{agentDisconnectedAtText}</strong>
        </p>
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
            <div className="agent-detail__launch-section">
              {agentTags.length > 0 && (
                <div className="agent-detail__tags-grid">
                  {agentTags.map((tag) => (
                    (() => {
                      const stackForTag =
                        agentStacks.find((stack) => stack.tag === tag) ?? null;
                      const isInProgressTag =
                        isPendingOperationStatus(stackForTag?.operationStatus);
                      const isSuccessTag =
                        stackForTag?.operationStatus === "success" && !!stackForTag?.running;
                      const isStoppedTag =
                        !isInProgressTag &&
                        !!stackForTag &&
                        !stackForTag.running;
                      const tagCardClassName = isInProgressTag
                        ? "agent-detail__tag-card agent-detail__tag-card--progress"
                        : isSuccessTag
                          ? "agent-detail__tag-card agent-detail__tag-card--active"
                          : isStoppedTag
                            ? "agent-detail__tag-card agent-detail__tag-card--stopped"
                          : "agent-detail__tag-card";
                      const tagButtonClassName = isInProgressTag
                        ? "agent-detail__tag-nav-btn agent-detail__tag-nav-btn--progress"
                        : isSuccessTag
                          ? "agent-detail__tag-nav-btn agent-detail__tag-nav-btn--active"
                          : isStoppedTag
                            ? "agent-detail__tag-nav-btn agent-detail__tag-nav-btn--stopped"
                          : "agent-detail__tag-nav-btn";

                      return (
                        <div key={tag} className={tagCardClassName}>
                          <button
                            type="button"
                            className={tagButtonClassName}
                            disabled={isManagementLocked}
                            onClick={() =>
                              void navigate(
                                `/agents/${encodeURIComponent(hostName)}/${encodeURIComponent(tag)}`
                              )
                            }
                          >
                            {tag}
                          </button>
                          {isSuccessTag && stackForTag?.serviceLinks?.admin && (
                            <a
                              href={stackForTag.serviceLinks.admin}
                              target="_blank"
                              rel="noreferrer"
                              className="agent-detail__tag-admin-link"
                            >
                              Админка
                            </a>
                          )}
                        </div>
                      );
                    })()
                  ))}
                </div>
              )}
              <button
                type="button"
                className="agent-detail__add-project"
                disabled={readonlyAgent}
                onClick={() => setIsLaunchPanelOpen((value) => !value)}
              >
                {isLaunchPanelOpen ? "Скрыть форму проекта" : "Добавить новый проект"}
              </button>
              {isLaunchPanelOpen && (
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
                      {tagItemsForHost.map((item) => (
                        <option key={item.tag} value={item.tag} />
                      ))}
                    </datalist>
                    {tagsError && (
                      <p className="agent-detail__error">{tagsError}</p>
                    )}
                    <button
                      type="button"
                      className="agent-detail__refresh"
                      disabled={tagsLoading || launching}
                      onClick={() => void loadTags(hostName)}
                    >
                      {tagsLoading ? "…" : "Обновить теги"}
                    </button>
                    <button
                      type="submit"
                      className="agent-detail__submit"
                      disabled={launching || readonlyAgent}
                    >
                      {launching ? "…" : "Запустить сервис"}
                    </button>
                  </form>
                </div>
              )}
            </div>
            <div className="agent-detail__launch-feedback">
              {launchError && <p className="agent-detail__error">{launchError}</p>}
              {launchOk && <p className="agent-detail__ok">{launchOk}</p>}
              {launchLinks && !isManagedProjectLoading && (
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
          <p className={managedOperationStatusClassName}>
            Статус запуска:{" "}
            <strong>
              {managedOperationStatus}
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

          {shouldShowManagedLinks && managedLinks ? (
            <ServiceLinksBlock links={managedLinks} />
          ) : (
            <div className="agent-detail__panel-links">
              <p className="agent-detail__panel-title">Ссылки сервисов</p>
              {!isManagedProjectReady || linkEntries.length === 0 ? (
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

          <div className="agent-detail__project-actions">
            <button
              type="button"
              className="agent-detail__restart-project"
              disabled={!canRestartManagedProject}
              onClick={() => void onRestartManagedProject()}
            >
              {restartingProject ? "Перезагружаем..." : "Перезагрузить сервис"}
            </button>
            <button
              type="button"
              className={
                shouldRenderPauseAction
                  ? "agent-detail__pause-project"
                  : "agent-detail__pause-project agent-detail__pause-project--start"
              }
              disabled={!(isManagedProjectRunning ? canPauseManagedProject : canStartManagedProject)}
              onClick={() =>
                void (isManagedProjectRunning
                  ? onPauseManagedProject()
                  : onStartManagedProject())
              }
            >
              {pausingProject
                ? shouldRenderPauseAction
                  ? "Останавливаем..."
                  : "Запускаем..."
                : shouldRenderPauseAction
                  ? "Остановить сервис"
                  : "Запустить сервис"}
            </button>
            <button
              type="button"
              className="agent-detail__stop-project"
              disabled={!canDeleteManagedProject}
              onClick={() => void onStopManagedProject()}
            >
              {stoppingProject ? "Удаляем..." : "Удалить сервис"}
            </button>
          </div>

          <div className="agent-detail__domains-section">
            <button
              type="button"
              className="agent-detail__add-project agent-detail__add-project--domains"
              disabled={!hasManagedProject || readonlyAgent}
              onClick={() => setIsDomainsPanelOpen((value) => !value)}
            >
              {isDomainsPanelOpen ? "Скрыть форму доменов" : "Привязать домен"}
            </button>

            {isDomainsPanelOpen && (
              <div className="agent-detail__launch-layout">
                <form
                  className="agent-detail__form agent-detail__form--launch"
                  onSubmit={(e) => void onAddDomains(e)}
                >
                  <label className="agent-detail__label" htmlFor="agent-service-domains">
                    Домены через запятую
                  </label>
                  <input
                    id="agent-service-domains"
                    type="text"
                    className="agent-detail__input"
                    value={domainsInput}
                    onChange={(e) => setDomainsInput(e.target.value)}
                    placeholder="admin.vseupalo.ru, portal.vseupalo.ru"
                    autoComplete="off"
                  />
                  {domainsError && (
                    <p className="agent-detail__error">{domainsError}</p>
                  )}
                  {domainsOk && (
                    <p className="agent-detail__ok">{domainsOk}</p>
                  )}
                  <button
                    type="submit"
                    className="agent-detail__submit"
                    disabled={domainsSubmitting || readonlyAgent}
                  >
                    {domainsSubmitting ? "…" : "Сохранить домены"}
                  </button>
                </form>
              </div>
            )}

            <div className="agent-detail__ssl-summary">
              <p className="agent-detail__panel-title">SSL сертификаты</p>
              {managedCertificates.length === 0 ? (
                <p className="agent-detail__hint">
                  Данные по SSL появятся после проверки сертификатов.
                </p>
              ) : (
                <div className="agent-detail__certificates">
                  {managedCertificates.map((certificate) => (
                    <div
                      key={certificate.domain}
                      className={certificateStatusClassName(certificate)}
                    >
                      <p className="agent-detail__certificate-title">{certificate.domain}</p>
                      <p className="agent-detail__certificate-line">
                        Начало: <strong>{formatCertificateDate(certificate.notBeforeUtc)}</strong>
                      </p>
                      <p className="agent-detail__certificate-line">
                        Конец: <strong>{formatCertificateDate(certificate.notAfterUtc)}</strong>
                      </p>
                      <p className="agent-detail__certificate-line">
                        Осталось дней: <strong>{certificate.daysLeft ?? "—"}</strong>
                      </p>
                      {certificate.error && (
                        <p className="agent-detail__error">{certificate.error}</p>
                      )}
                      <div className="agent-detail__certificate-actions">
                        <button
                          type="button"
                          className="agent-detail__domain-remove agent-detail__domain-remove--card"
                          disabled={readonlyAgent || removingDomain === certificate.domain}
                          onClick={() => void onDeleteDomain(certificate.domain)}
                          aria-label={`Удалить домен ${certificate.domain}`}
                        >
                          {removingDomain === certificate.domain ? "…" : "×"}
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        </section>
      )}
    </div>
  );
}
