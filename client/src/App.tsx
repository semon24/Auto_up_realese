import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api, errMessage } from "./apiClient";
import { STATUS_POLL_MS, TAGS_POLL_MS } from "./constants";
import { dispatchApp, useAppStore } from "./store/appStore";
import type { StatusResponse, TagsResponse } from "./types";
import "./App.css";

export default function App() {
  const MAX_ERROR_CHARS = 700;
  const {
    items,
    tagsError,
    tagsLoading,
    status,
    selectedTagView,
    query,
    open,
    actionError,
  } = useAppStore();
  const wrapRef = useRef<HTMLDivElement>(null);
  const [pendingStartTags, setPendingStartTags] = useState<string[]>([]);
  const [pendingStopTags, setPendingStopTags] = useState<string[]>([]);

  const loadTags = useCallback(async () => {
    dispatchApp({ type: "TAGS_REQUEST" });
    try {
      const data = await api<TagsResponse>("/api/tags");
      dispatchApp({ type: "TAGS_SUCCESS", items: data.items ?? [] });
    } catch (e) {
      dispatchApp({ type: "TAGS_FAILURE", error: errMessage(e) });
    }
  }, []);

  const loadStatus = useCallback(async () => {
    try {
      const data = await api<StatusResponse>("/api/status");
      dispatchApp({
        type: "STATUS_SUCCESS",
        running: !!data.running,
        stacks: data.stacks ?? [],
      });
    } catch {
      /* ignore */
    }
  }, []);

  useEffect(() => {
    void loadTags();
    const id = setInterval(() => void loadTags(), TAGS_POLL_MS);
    return () => clearInterval(id);
  }, [loadTags]);

  useEffect(() => {
    void loadStatus();
    const id = setInterval(() => void loadStatus(), STATUS_POLL_MS);
    return () => clearInterval(id);
  }, [loadStatus]);

  useEffect(() => {
    function onDoc(e: MouseEvent) {
      const target = e.target;
      if (
        wrapRef.current &&
        target instanceof Node &&
        !wrapRef.current.contains(target)
      ) {
        dispatchApp({ type: "OPEN_SET", open: false });
      }
    }
    document.addEventListener("mousedown", onDoc);
    return () => document.removeEventListener("mousedown", onDoc);
  }, []);

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
  const isCurrentTagStarting = !!effectiveTag && pendingStartTags.includes(effectiveTag);
  const isSelectedTagStopping = !!selectedTagView && pendingStopTags.includes(selectedTagView);
  const stackErrors = useMemo(
    () =>
      status.stacks.filter(
        (stack) => typeof stack.operationError === "string" && stack.operationError.trim().length > 0
      ),
    [status.stacks]
  );
  const selectedServices = useMemo(
    () => Object.entries(selectedStack?.services ?? {}),
    [selectedStack]
  );
  const formatErrorForUi = useCallback((raw: string) => {
    const normalized = raw.replace(/\r\n/g, "\n").trim();
    if (normalized.length <= MAX_ERROR_CHARS) return normalized;
    return `...${normalized.slice(-(MAX_ERROR_CHARS - 3))}`;
  }, []);

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
      <aside className="stack-sidebar">
        <button
          type="button"
          className={`stack-main-btn${selectedTagView ? "" : " is-active"}`}
          onClick={() => dispatchApp({ type: "SELECT_TAG_VIEW", tag: null })}
        >
          Main
        </button>
        <div className="stack-list">
          {status.stacks.map((stack) => {
            const isLoading =
              stack.operationStatus === "in_progress" ||
              stack.operationStatus === "deleting";
            const cardClass = isLoading
              ? "stack-card is-loading"
              : stack.running
                ? "stack-card is-running"
                : "stack-card";
            return (
              <div key={stack.tag} className="stack-card-wrap">
                <button
                  type="button"
                  className={`${cardClass}${selectedTagView === stack.tag ? " is-active" : ""}`}
                  onClick={() => {
                    dispatchApp({ type: "SELECT_TAG_VIEW", tag: stack.tag });
                    dispatchApp({ type: "QUERY_CHANGE", query: stack.tag });
                  }}
                >
                  <span className="stack-card__tag">{stack.tag}</span>
                  <span className="stack-card__state">
                    {isLoading ? "loading" : stack.running ? "running" : "idle"}
                  </span>
                </button>
              </div>
            );
          })}
        </div>
      </aside>

      {!selectedTagView && <h1 className="title">Выберите тег образа для запуска</h1>}

      {!selectedTagView ? (
        <div className="combo-wrap" ref={wrapRef}>
          <input
            className="search"
            type="search"
            placeholder="Поиск по тегу…"
            value={query}
            onChange={(e) => {
              dispatchApp({ type: "QUERY_CHANGE", query: e.target.value });
              dispatchApp({ type: "OPEN_SET", open: true });
            }}
            onFocus={() => dispatchApp({ type: "OPEN_SET", open: true })}
            autoComplete="off"
            aria-expanded={open}
            aria-controls="tag-listbox"
          />
          {open && filtered.length > 0 && (
            <ul id="tag-listbox" className="list" role="listbox">
              {filtered.map((x) => (
                <li
                  key={x.tag}
                  role="option"
                  aria-selected={effectiveTag === x.tag}
                  onMouseDown={(e) => e.preventDefault()}
                  onClick={() => pickItem(x.tag)}
                >
                  {x.tag}
                </li>
              ))}
            </ul>
          )}
        </div>
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
          disabled={tagsLoading}
          onClick={() => void loadTags()}
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
        <div className="main-errors">
          {stackErrors.map((stack, index) => (
            <div key={stack.tag} className="main-errors__item">
              {index > 0 && <hr className="main-errors__divider" />}
              <p className="main-errors__title">Ошибка для тега: {stack.tag}</p>
              <pre className="main-errors__text">
                {formatErrorForUi(stack.operationError ?? "")}
              </pre>
            </div>
          ))}
        </div>
      )}

      {selectedTagView &&
        selectedStack?.operationStatus === "success" &&
        selectedStack.serviceLinks && (
        <div className="service-links">
          <p className="service-links__title">Сервисы</p>
          <ul className="service-links__list">
            {selectedStack.serviceLinks.admin && (
              <li>
                <a
                  href={selectedStack.serviceLinks.admin}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  Admin
                </a>
              </li>
            )}
            {selectedStack.serviceLinks.server && (
              <li>
                <a
                  href={selectedStack.serviceLinks.server}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  Server
                </a>
              </li>
            )}
            {selectedStack.serviceLinks.call && (
              <li>
                <a
                  href={selectedStack.serviceLinks.call}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  Call
                </a>
              </li>
            )}
            {selectedStack.serviceLinks.portal && (
              <li>
                <a
                  href={selectedStack.serviceLinks.portal}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  Portal
                </a>
              </li>
            )}
          </ul>
        </div>
      )}

      {selectedTagView && selectedServices.length > 0 && (
        <div className="service-states">
          <p className="service-states__title">Состояние сервисов</p>
          <table className="service-states__table">
            <thead>
              <tr>
                <th>Название</th>
                <th>State</th>
                <th>Health</th>
              </tr>
            </thead>
            <tbody>
              {selectedServices.map(([serviceName, service]) => (
                <tr key={serviceName}>
                  <td className="service-states__name">{serviceName}</td>
                  <td>{service.state}</td>
                  <td>{service.health ?? "-"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
