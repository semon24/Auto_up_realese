import { useCallback, useMemo, useRef, useState } from "react";
import { api, errMessage } from "../apiClient";
import { ServiceLinks } from "../components/ServiceLinks/ServiceLinks";
import { ServiceStatesTable } from "../components/ServiceStatesTable/ServiceStatesTable";
import { StackErrors } from "../components/StackErrors/StackErrors";
import { StackSidebar } from "../components/StackSidebar/StackSidebar";
import { TagSelector } from "../components/TagSelector/TagSelector";
import { useOutsideClick } from "../hooks/useOutsideClick";
import { usePolling } from "../pollingContext";
import { dispatchApp, useAppStore } from "../store/appStore";
import { formatErrorForUi } from "../utils/formatErrorForUi";
import { AGENT_STATUS_PASSWORD_ACCEPTED } from "../constants";
import "./MainPage.css";

const MAX_ERROR_CHARS = 700;

export function MainPage() {
  const {
    tagsByHost,
    tagsLoadingHost,
    tagsErrorByHost,
    tagsClientError,
    status,
    selectedTagView,
    query,
    open,
    actionError,
  } = useAppStore();
  const wrapRef = useRef<HTMLDivElement>(null);
  const [pendingStartTags, setPendingStartTags] = useState<string[]>([]);
  const [pendingStopTags, setPendingStopTags] = useState<string[]>([]);
  const { loadTags, loadStatus } = usePolling();

  const acceptableHostsForTags = useMemo(
    () =>
      Object.entries(status.agents)
        .filter(([, st]) => st === AGENT_STATUS_PASSWORD_ACCEPTED)
        .map(([hostName]) => hostName.trim())
        .filter((hostName) => hostName.length > 0)
        .sort(),
    [status.agents]
  );

  const singleTagsHost =
    acceptableHostsForTags.length === 1 ? acceptableHostsForTags[0] : null;

  const items = singleTagsHost
    ? tagsByHost[singleTagsHost] ?? []
    : [];

  const tagsError =
    tagsClientError ??
    (singleTagsHost ? tagsErrorByHost[singleTagsHost] : null);

  const tagsLoading = Boolean(
    singleTagsHost && tagsLoadingHost === singleTagsHost
  );

  const closeTagPicker = useCallback(() => {
    dispatchApp({ type: "OPEN_SET", open: false });
  }, []);
  useOutsideClick(wrapRef, closeTagPicker);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return items;
    return items.filter((x) => x.tag.toLowerCase().includes(q));
  }, [items, query]);

  const effectiveTag = useMemo(() => {
    const q = query.trim();
    const hit = items.find((i) => i.tag === q);
    return hit ? hit.tag : null;
  }, [query, items]);

  const buttonMode = useMemo(() => {
    if (selectedTagView) {
      const selected = status.stacks.find((s) => s.tag === selectedTagView);
      const selectedLoading =
        selected?.operationStatus === "in_progress" ||
        selected?.operationStatus === "deleting";
      if (selectedLoading || selected?.running) return "stop";
      return "idle";
    }
    if (effectiveTag) return "start";
    return "idle";
  }, [selectedTagView, status.stacks, effectiveTag]);

  const displayTag = selectedTagView ?? query.trim();
  const selectedStack = useMemo(
    () => status.stacks.find((s) => s.tag === selectedTagView) ?? null,
    [status.stacks, selectedTagView]
  );
  const isCurrentTagStarting =
    !!effectiveTag && pendingStartTags.includes(effectiveTag);
  const isSelectedTagStopping =
    !!selectedTagView && pendingStopTags.includes(selectedTagView);
  const stackErrors = useMemo(
    () =>
      status.stacks.filter(
        (stack) =>
          typeof stack.operationError === "string" &&
          stack.operationError.trim().length > 0
      ),
    [status.stacks]
  );
  const selectedServices = useMemo(
    () => Object.entries(selectedStack?.services ?? {}),
    [selectedStack]
  );
  const formatStackError = useCallback(
    (raw: string) => formatErrorForUi(raw, MAX_ERROR_CHARS),
    []
  );

  async function onPrimaryClick() {
    dispatchApp({ type: "ACTION_ERROR_SET", error: null });
    if (selectedTagView && selectedStack?.running) {
      if (pendingStopTags.includes(selectedTagView)) return;
      setPendingStopTags((prev) => [...prev, selectedTagView]);
      try {
        await api("/api/stop", {
          method: "POST",
          body: JSON.stringify({ tag: selectedTagView }),
        });
        dispatchApp({ type: "SELECT_TAG_VIEW", tag: null });
        await loadStatus();
      } catch (e) {
        dispatchApp({ type: "ACTION_ERROR_SET", error: errMessage(e) });
      } finally {
        setPendingStopTags((prev) =>
          prev.filter((tag) => tag !== selectedTagView)
        );
      }
      return;
    }
    if (!effectiveTag) return;
    if (pendingStartTags.includes(effectiveTag)) return;
    setPendingStartTags((prev) => [...prev, effectiveTag]);
    try {
      await api("/api/start", {
        method: "POST",
        body: JSON.stringify({ tag: effectiveTag }),
      });
      await loadStatus();
    } catch (e) {
      dispatchApp({ type: "ACTION_ERROR_SET", error: errMessage(e) });
    } finally {
      setPendingStartTags((prev) => prev.filter((tag) => tag !== effectiveTag));
    }
  }

  function pickItem(tag: string) {
    dispatchApp({ type: "PICK_TAG", tag });
  }

  const primaryLabel =
    selectedTagView && selectedStack?.running && !isSelectedTagStopping
      ? "Выключить проект"
      : "Поднять проект";

  const btnClass =
    buttonMode === "idle"
      ? "btn btn--idle"
      : buttonMode === "start"
        ? "btn btn--start"
        : "btn btn--stop";

  return (
    <div className="page">
      <StackSidebar
        stacks={status.stacks}
        selectedTagView={selectedTagView}
        onSelectMain={() => dispatchApp({ type: "SELECT_TAG_VIEW", tag: null })}
        onSelectStack={(tag) => {
          dispatchApp({ type: "SELECT_TAG_VIEW", tag });
          dispatchApp({ type: "QUERY_CHANGE", query: tag });
        }}
      />

      {!selectedTagView && (
        <h1 className="title">Выберите тег образа для запуска</h1>
      )}

      {!selectedTagView ? (
        <TagSelector
          wrapRef={wrapRef}
          query={query}
          open={open}
          filtered={filtered}
          effectiveTag={effectiveTag}
          onQueryChange={(value) => {
            dispatchApp({ type: "QUERY_CHANGE", query: value });
            dispatchApp({ type: "OPEN_SET", open: true });
          }}
          onOpen={() => dispatchApp({ type: "OPEN_SET", open: true })}
          onPick={pickItem}
        />
      ) : (
        <div className="selected-tag-view">
          <span className="selected-tag-label">Текущий тег:</span>
          <strong>{displayTag}</strong>
        </div>
      )}

      {!selectedTagView && (
        <button
          type="button"
          className="btn btn--refresh"
          disabled={tagsLoading || !singleTagsHost}
          onClick={() => {
            if (!singleTagsHost) {
              dispatchApp({
                type: "TAGS_FAILURE",
                hostName: "",
                error:
                  acceptableHostsForTags.length === 0
                    ? "Нет агента с принятым паролем — теги не к кому запросить."
                    : "Несколько агентов подключены. Откройте карточку нужного хоста и нажмите «Обновить теги» там.",
              });
              return;
            }
            void loadTags(singleTagsHost);
          }}
        >
          {tagsLoading ? "…" : "Обновить список тегов"}
        </button>
      )}

      {tagsError && <p className="error">{tagsError}</p>}
      {!tagsError && items.length === 0 && (
        <p className="hint">Теги в Harbor пока не найдены или список пуст.</p>
      )}

      <button
        type="button"
        className={btnClass}
        disabled={
          (selectedTagView
            ? isSelectedTagStopping || !selectedStack?.running
            : !effectiveTag || isCurrentTagStarting) ||
          !!tagsError
        }
        onClick={() => void onPrimaryClick()}
      >
        {selectedTagView && isSelectedTagStopping
          ? "…"
          : !selectedTagView && isCurrentTagStarting
            ? "Запускается..."
            : primaryLabel}
      </button>

      {actionError && <p className="error">{actionError}</p>}
      {!selectedTagView && stackErrors.length > 0 && (
        <StackErrors items={stackErrors} formatError={formatStackError} />
      )}

      {selectedTagView &&
        selectedStack?.operationStatus === "success" &&
        selectedStack.serviceLinks && (
          <ServiceLinks links={selectedStack.serviceLinks} />
        )}

      {selectedTagView && <ServiceStatesTable items={selectedServices} />}
    </div>
  );
}
