import { useCallback, useEffect, useMemo, useRef } from "react";
import { api, errMessage } from "./apiClient";
import { STATUS_POLL_MS, TAGS_POLL_MS } from "./constants";
import { dispatchApp, useAppStore } from "./store/appStore";
import type { StatusResponse, TagsResponse } from "./types";
import "./App.css";

export default function App() {
  const {
    items,
    tagsError,
    tagsLoading,
    status,
    query,
    open,
    actionError,
    loading,
  } = useAppStore();
  const wrapRef = useRef<HTMLDivElement>(null);

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
        activeTag: data.activeTag ?? null,
        serviceLinks: data.serviceLinks ?? null,
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
    if (status.running) return "stop";
    if (effectiveTag) return "start";
    return "idle";
  }, [status.running, effectiveTag]);

  async function onPrimaryClick() {
    dispatchApp({ type: "ACTION_ERROR_SET", error: null });
    if (status.running) {
      dispatchApp({ type: "LOADING_SET", loading: true });
      try {
        await api("/api/stop", { method: "POST" });
        await loadStatus();
      } catch (e) {
        dispatchApp({ type: "ACTION_ERROR_SET", error: errMessage(e) });
      } finally {
        dispatchApp({ type: "LOADING_SET", loading: false });
      }
      return;
    }
    if (!effectiveTag) return;
    dispatchApp({ type: "LOADING_SET", loading: true });
    try {
      await api("/api/start", {
        method: "POST",
        body: JSON.stringify({ tag: effectiveTag }),
      });
      await loadStatus();
    } catch (e) {
      dispatchApp({ type: "ACTION_ERROR_SET", error: errMessage(e) });
    } finally {
      dispatchApp({ type: "LOADING_SET", loading: false });
    }
  }

  function pickItem(tag: string) {
    dispatchApp({ type: "PICK_TAG", tag });
  }

  const primaryLabel = status.running
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
      <h1 className="title">Выберите тег образа для запуска</h1>

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

      <button
        type="button"
        className="btn btn--refresh"
        disabled={tagsLoading}
        onClick={() => void loadTags()}
      >
        {tagsLoading ? "…" : "Обновить список тегов"}
      </button>

      {tagsError && <p className="error">{tagsError}</p>}
      {!tagsError && items.length === 0 && (
        <p className="hint">Теги в Harbor пока не найдены или список пуст.</p>
      )}

      <button
        type="button"
        className={btnClass}
        disabled={loading || (!status.running && !effectiveTag) || !!tagsError}
        onClick={() => void onPrimaryClick()}
      >
        {loading ? "…" : primaryLabel}
      </button>

      {actionError && <p className="error">{actionError}</p>}

      <p className="meta">
        Сейчас в .env записан тег:{" "}
        <strong>{status.activeTag ?? "—"}</strong>
        {status.running ? " · compose запущен" : ""}
      </p>

      {status.running && status.serviceLinks && (
        <div className="service-links">
          <p className="service-links__title">Сервисы</p>
          <ul className="service-links__list">
            {status.serviceLinks.admin && (
              <li>
                <a
                  href={status.serviceLinks.admin}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  Admin
                </a>
              </li>
            )}
            {status.serviceLinks.server && (
              <li>
                <a
                  href={status.serviceLinks.server}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  Server
                </a>
              </li>
            )}
            {status.serviceLinks.call && (
              <li>
                <a
                  href={status.serviceLinks.call}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  Call
                </a>
              </li>
            )}
            {status.serviceLinks.portal && (
              <li>
                <a
                  href={status.serviceLinks.portal}
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
    </div>
  );
}
