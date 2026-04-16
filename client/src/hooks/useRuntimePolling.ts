import { useCallback, useEffect } from "react";
import { api, errMessage } from "../apiClient";
import { STATUS_POLL_MS, TAGS_POLL_MS } from "../constants";
import { dispatchApp } from "../store/appStore";
import type { StatusResponse, TagsResponse } from "../types";

export function useRuntimePolling() {
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
      /* ignore status polling errors */
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

  return { loadTags, loadStatus };
}
