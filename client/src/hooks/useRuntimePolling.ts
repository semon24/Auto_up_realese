import { useCallback, useEffect } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { api, errMessage } from "../apiClient";
import { STATUS_POLL_MS, TAGS_POLL_MS } from "../constants";
import { dispatchApp } from "../store/appStore";
import type { AgentUpdatedEvent, StatusResponse, TagsResponse } from "../types";

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

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/agents")
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    const syncAgentsSnapshot = async () => {
      const snapshot =
        await connection.invoke<Record<string, string>>("GetAgentsSnapshot");
      dispatchApp({ type: "AGENTS_SNAPSHOT", agents: snapshot ?? {} });
    };

    connection.on("agent_updated", (payload: AgentUpdatedEvent) => {
      const hostName = payload?.hostName?.trim();
      if (!hostName) return;
      dispatchApp({
        type: "AGENT_UPDATED",
        hostName,
        status: payload.status ?? null,
      });
    });
    connection.onreconnected(() => syncAgentsSnapshot());

    const startConnection = async () => {
      try {
        await connection.start();
        await syncAgentsSnapshot();
      } catch {
        // Авто-reconnect покрывает временные сбои сети/бэкенда.
      }
    };

    void startConnection();

    return () => {
      connection.off("agent_updated");
      void connection.stop();
    };
  }, []);

  return { loadTags, loadStatus };
}
