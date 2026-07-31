import { useEffect, useMemo, useState, type FormEvent } from "react";
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
import type { RegistryChannel, ServiceLinks, SslCertificateInfo } from "../types";
import { ServiceStatesTable } from "../components/ServiceStatesTable/ServiceStatesTable";
import { ServiceLinks as ServiceLinksBlock } from "../components/ServiceLinks/ServiceLinks";
import { buildTagCacheKey } from "../utils/registryChannel";

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

function isSingleProjectAgent(mode?: string | null) {
  const normalized = mode?.trim().toLowerCase();
  return normalized === "single-project" || normalized === "single_project" || normalized === "singleproject";
}

function isNewSoloProjectAgent(mode?: string | null) {
  const normalized = mode?.trim().toLowerCase();
  return normalized === "new-solo-project" || normalized === "new_solo_project" || normalized === "newsoloproject";
}

function isMultiProjectAgent(mode?: string | null) {
  const normalized = mode?.trim().toLowerCase();
  return normalized === "multy_project" ||
    normalized === "multi_project" ||
    normalized === "multy-project" ||
    normalized === "multi-project";
}

function buildStackName(projectName: string) {
  const normalized = projectName
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .replace(/-{2,}/g, "-");

  const randomSuffix = Math.floor(100000 + Math.random() * 900000).toString();
  return normalized ? `${normalized}-${randomSuffix}` : randomSuffix;
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
  const [registryChannel, setRegistryChannel] = useState<RegistryChannel>("stage");

  const {
    status,
    tagsByHost,
    tagsLoadingHost,
    tagsErrorByHost,
  } = useAppStore();

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
  const singleProjectAgent = isSingleProjectAgent(agentInfo?.mode);
  const newSoloProjectAgent = isNewSoloProjectAgent(agentInfo?.mode);
  const multiProjectAgent = isMultiProjectAgent(agentInfo?.mode);
  const tagsCacheKey = buildTagCacheKey(
    hostName,
    multiProjectAgent ? registryChannel : undefined
  );
  const tagItemsForHost = tagsByHost[tagsCacheKey] ?? [];
  const tagsLoading = tagsLoadingHost === tagsCacheKey;
  const tagsError = tagsErrorByHost[tagsCacheKey] ?? null;
  const agentDisconnectedAtText = useMemo(() => {
    const raw = agentInfo?.disconnectedAtUtc?.trim();
    if (!raw) return null;

    const parsed = new Date(raw);
    if (Number.isNaN(parsed.getTime())) return raw;

    return parsed.toLocaleString("ru-RU");
  }, [agentInfo?.disconnectedAtUtc]);
  const agentStacks = status.stacksByHost[hostName] ?? [];
  const channelStacks = useMemo(
    () => multiProjectAgent
      ? agentStacks.filter((stack) => stack.registryChannel === registryChannel)
      : agentStacks,
    [agentStacks, multiProjectAgent, registryChannel]
  );
  const [password, setPassword] = useState("");
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [launchProjectName, setLaunchProjectName] = useState("");
  const [launchVersion, setLaunchVersion] = useState("");
  const [launchDomain, setLaunchDomain] = useState("");
  const [launching, setLaunching] = useState(false);
  const [launchError, setLaunchError] = useState<string | null>(null);
  const [launchOk, setLaunchOk] = useState<string | null>(null);
  const [launchLinks, setLaunchLinks] = useState<ServiceLinks | null>(null);
  const [isLaunchPanelOpen, setIsLaunchPanelOpen] = useState(false);
  const [lastStartedTag, setLastStartedTag] = useState<string | null>(null);
  const [managedProjectOverride, setManagedProjectOverride] = useState<ManagedProjectOverride | null>(null);
  const [pausingProject, setPausingProject] = useState(false);
  const [startingProject, setStartingProject] = useState(false);
  const [stoppingProject, setStoppingProject] = useState(false);
  const [restartingProject, setRestartingProject] = useState(false);
  const [updatingVersion, setUpdatingVersion] = useState(false);
  const [updateVersion, setUpdateVersion] = useState("");
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
  const initializedNewSoloStack = newSoloProjectAgent
    ? agentStacks.find((stack) => !!stack.version?.trim()) ?? null
    : null;
  const newSoloFallbackTag = initializedNewSoloStack?.tag ?? null;
  const managedTag = selectedTagFromRoute || lastStartedTag || runningTagFromStatus || newSoloFallbackTag;
  const managedStack = useMemo(
    () => agentStacks.find((stack) => stack.tag === managedTag) ?? null,
    [managedTag, agentStacks]
  );
  useEffect(() => {
    const projectChannel = managedStack?.registryChannel;
    if (isTagRoute && multiProjectAgent && (projectChannel === "stage" || projectChannel === "release")) {
      setRegistryChannel(projectChannel);
    }
  }, [isTagRoute, managedStack?.registryChannel, multiProjectAgent]);
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
          channelStacks
            .map((stack) => stack.tag)
            .filter((tag) => typeof tag === "string" && tag.trim().length > 0)
        )
      ),
    [channelStacks]
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
    pausingProject || startingProject || stoppingProject || restartingProject || updatingVersion;
  const isManagedProjectStarting =
    effectiveManagedOperationStatus === "starting" ||
    (isManagedProjectLoading && !isManagedProjectRunning);
  const shouldRenderPauseAction =
    isManagedProjectRunning ||
    (isPendingOperationStatus(effectiveManagedOperationStatus) && !isManagedProjectStarting);
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
  const canUpdateManagedVersion =
    hasManagedProject &&
    updateVersion.trim().length > 0 &&
    !isManagedProjectLoading &&
    !isProjectActionPending &&
    !readonlyAgent;
  const canInitializeNewSoloProject =
    newSoloProjectAgent &&
    !initializedNewSoloStack &&
    !isManagedProjectLoading &&
    !readonlyAgent;
  const visibleAgentTags = newSoloProjectAgent && canInitializeNewSoloProject
    ? []
    : agentTags;

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

  function onToggleLaunchPanel() {
    if (readonlyAgent) return;
    setIsLaunchPanelOpen((value) => {
      const nextValue = !value;
      if (nextValue) {
        void loadTags(hostName, multiProjectAgent ? registryChannel : undefined);
      }
      return nextValue;
    });
  }

  function onRegistryChannelChange(nextChannel: RegistryChannel) {
    if (nextChannel === registryChannel) return;

    setRegistryChannel(nextChannel);
    setLaunchVersion("");
    setUpdateVersion("");
    setLaunchError(null);
    setLaunchOk(null);
    void loadTags(hostName, nextChannel);
  }

  async function onLaunch(e: FormEvent) {
    e.preventDefault();
    if (readonlyAgent) return;
    setLaunchError(null);
    setLaunchOk(null);
    setLaunchLinks(null);
    const projectName = launchProjectName.trim();
    const version = launchVersion.trim();
    const domain = launchDomain.trim();

    if (newSoloProjectAgent && !canInitializeNewSoloProject) {
      setLaunchError("Solo-project already initialized");
      return;
    }

    if (!newSoloProjectAgent && !projectName) {
      setLaunchError("Введите имя проекта");
      return;
    }

    if (!version) {
      setLaunchError("Введите версию для запуска");
      return;
    }
    const stackName = newSoloProjectAgent
      ? buildStackName("single-project")
      : buildStackName(projectName);
    setManagedProjectOverride(null);
    setLaunching(true);
    try {
      const res = await api<DockerComposeUpResponse>(
        `/api/agents/${encodeURIComponent(hostName)}/docker-compose-up`,
        {
          method: "POST",
          body: JSON.stringify({
            stackName,
            version,
            domain: domain || null,
            registryChannel: multiProjectAgent ? registryChannel : null,
          }),
        }
      );
      setLaunchOk("Команда запуска отправлена агенту.");
      setLaunchLinks(res.payload?.serviceLinks ?? null);
      setLastStartedTag(stackName);
      void navigate(
        `/agents/${encodeURIComponent(hostName)}/${encodeURIComponent(stackName)}`
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
          body: JSON.stringify({ stackName: managedTag }),
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
          body: JSON.stringify({ stackName: managedTag }),
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
    const version = managedStack?.version?.trim();
    if (!version) {
      setLaunchError("Для этого стека не найдена версия. Обновите статус.");
      return;
    }
    setStartingProject(true);
    try {
      const res = await api<DockerComposeUpResponse>(
        `/api/agents/${encodeURIComponent(hostName)}/docker-compose-up`,
        {
          method: "POST",
          body: JSON.stringify({ stackName: managedTag, version }),
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
      setStartingProject(false);
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
          body: JSON.stringify({ stackName: managedTag }),
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

  async function onUpdateManagedVersion(e: FormEvent) {
    e.preventDefault();
    if (!managedTag) return;
    if (readonlyAgent) return;

    const version = updateVersion.trim();
    if (!version) {
      setLaunchError("Введите версию для обновления");
      return;
    }

    setLaunchError(null);
    setLaunchOk(null);
    setManagedProjectOverride(null);
    setUpdatingVersion(true);
    try {
      await api<{ ok?: boolean }>(
        `/api/agents/${encodeURIComponent(hostName)}/update-version`,
        {
          method: "POST",
          body: JSON.stringify({ stackName: managedTag, version }),
        }
      );
      setLaunchOk(`Версия ${managedTag} обновлена до ${version}.`);
      setManagedProjectOverride({
        tag: managedTag,
        running: true,
        operationStatus: "success",
      });
      await loadStatus();
    } catch (err) {
      setLaunchError(errMessage(err));
    } finally {
      setUpdatingVersion(false);
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
        {multiProjectAgent && !isTagRoute && (
          <div className="agent-detail__registry-switch" aria-label="Реестр образов">
            <span
              className={registryChannel === "stage" ? "agent-detail__registry-label agent-detail__registry-label--active" : "agent-detail__registry-label"}
            >
              stage
            </span>
            <label className="agent-detail__registry-toggle">
              <input
                type="checkbox"
                checked={registryChannel === "release"}
                onChange={(event) =>
                  onRegistryChannelChange(event.target.checked ? "release" : "stage")
                }
                aria-label="Переключить между stage и release"
              />
              <span className="agent-detail__registry-slider" />
            </label>
            <span
              className={registryChannel === "release" ? "agent-detail__registry-label agent-detail__registry-label--active" : "agent-detail__registry-label"}
            >
              release
            </span>
          </div>
        )}
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
              {visibleAgentTags.length > 0 && (
                <div className="agent-detail__tags-grid">
                  {visibleAgentTags.map((tag) => (
                    (() => {
                      const stackForTag =
                        agentStacks.find((stack) => stack.tag === tag) ?? null;
                      const stackVersion = stackForTag?.version?.trim() ?? "";
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
                            <span className="agent-detail__tag-nav-name">{tag}</span>
                            {stackVersion ? (
                              <span className="agent-detail__tag-nav-version">
                                {stackVersion}
                              </span>
                            ) : null}
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
              {!singleProjectAgent && (!newSoloProjectAgent || canInitializeNewSoloProject) && (
                <>
                  <button
                    type="button"
                    className="agent-detail__add-project"
                    disabled={readonlyAgent}
                    onClick={onToggleLaunchPanel}
                  >
                    {isLaunchPanelOpen ? "Скрыть форму проекта" : "Добавить новый проект"}
                  </button>
                  {isLaunchPanelOpen && (
                    <div className="agent-detail__launch-layout">
                      <form
                        className="agent-detail__form agent-detail__form--launch"
                        onSubmit={(e) => void onLaunch(e)}
                      >
                        <label
                          className="agent-detail__label"
                          htmlFor="agent-launch-project-name"
                          style={{ display: newSoloProjectAgent ? "none" : undefined }}
                        >
                          Имя проекта
                        </label>
                        <input
                          id="agent-launch-project-name"
                          type="text"
                          className="agent-detail__input"
                          style={{ display: newSoloProjectAgent ? "none" : undefined }}
                          value={launchProjectName}
                          onChange={(e) => setLaunchProjectName(e.target.value)}
                          placeholder="Например, demo"
                          autoComplete="off"
                        />
                        <label className="agent-detail__label" htmlFor="agent-launch-version">
                          Версия образа
                        </label>
                        <input
                          id="agent-launch-version"
                          list="agent-launch-tags"
                          type="text"
                          className="agent-detail__input"
                          value={launchVersion}
                          onChange={(e) => setLaunchVersion(e.target.value)}
                          placeholder="Введите или выберите версию"
                          autoComplete="off"
                        />
                        <datalist id="agent-launch-tags">
                          {tagItemsForHost.map((item) => (
                            <option key={item.tag} value={item.tag} />
                          ))}
                        </datalist>
                        <label className="agent-detail__label" htmlFor="agent-launch-domain">
                          Домен или IP
                        </label>
                        <input
                          id="agent-launch-domain"
                          type="text"
                          className="agent-detail__input"
                          value={launchDomain}
                          onChange={(e) => setLaunchDomain(e.target.value)}
                          placeholder="Необязательно. Если пусто, будет использован IP агента"
                          autoComplete="off"
                        />
                        {tagsError && (
                          <p className="agent-detail__error">{tagsError}</p>
                        )}
                        <button
                          type="button"
                          className="agent-detail__refresh"
                          disabled={tagsLoading || launching}
                          onClick={() => void loadTags(
                            hostName,
                            multiProjectAgent ? registryChannel : undefined
                          )}
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
                </>
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
          <div className="agent-detail__tag-head">
            <h2 className="agent-detail__tag-title">{managedTag}</h2>
            {managedStack?.version?.trim() ? (
              <p className="agent-detail__tag-version">
                Версия: <strong>{managedStack.version.trim()}</strong>
              </p>
            ) : null}
          </div>
          <form className="agent-detail__version-update" onSubmit={onUpdateManagedVersion}>
            <label className="agent-detail__label" htmlFor="agent-update-version">
              Новая версия образа
            </label>
            <div className="agent-detail__version-update-row">
              <input
                id="agent-update-version"
                list="agent-update-tags"
                type="text"
                className="agent-detail__input"
                value={updateVersion}
                onFocus={() => void loadTags(
                  hostName,
                  multiProjectAgent ? registryChannel : undefined
                )}
                onChange={(e) => setUpdateVersion(e.target.value)}
                placeholder={managedStack?.version?.trim() || "Введите или выберите версию"}
                autoComplete="off"
              />
              <datalist id="agent-update-tags">
                {tagItemsForHost.map((item) => (
                  <option key={item.tag} value={item.tag} />
                ))}
              </datalist>
              <button
                type="submit"
                className="agent-detail__update-version"
                disabled={!canUpdateManagedVersion}
              >
                {updatingVersion ? "Обновляем..." : "Обновить версию"}
              </button>
            </div>
            {tagsLoading && (
              <p className="agent-detail__hint">Загружаем версии из Harbor...</p>
            )}
            {tagsError && <p className="agent-detail__error">{tagsError}</p>}
          </form>
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
                ? "Останавливаем..."
                : startingProject || isManagedProjectStarting
                  ? "Запускаем..."
                : shouldRenderPauseAction
                  ? "Остановить сервис"
                  : "Запустить сервис"}
            </button>
            {!singleProjectAgent && !newSoloProjectAgent && (
              <button
                type="button"
                className="agent-detail__stop-project"
                disabled={!canDeleteManagedProject}
                onClick={() => void onStopManagedProject()}
              >
                {stoppingProject ? "Удаляем..." : "Удалить сервис"}
              </button>
            )}
          </div>

          {managedCertificates.length > 0 && (
            <div className="agent-detail__domains-section">
            <div className="agent-detail__ssl-summary">
              <p className="agent-detail__panel-title">SSL сертификаты</p>
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
                  </div>
                ))}
              </div>
            </div>
            </div>
          )}
        </section>
      )}
    </div>
  );
}
