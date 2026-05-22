import { useCallback, useEffect, useRef } from "react";
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import { errMessage } from "../apiClient";
import { AGENT_STATUS_DISCONNECTED } from "../constants";
import { dispatchApp } from "../store/appStore";
import type {
  AgentConnectionInfo,
  AgentUpdatedEvent,
  RuntimeSnapshotByHost,
  StatusUpdatedEvent,
} from "../types";
import { parseStatusUpdatedEvent } from "../utils/runtimeStatus";

function mapRuntimeSnapshotByHost(
  snapshot: RuntimeSnapshotByHost | null | undefined
): RuntimeSnapshotByHost {
  if (!snapshot) return {};

  const result: RuntimeSnapshotByHost = {};
  for (const [hostName, stacks] of Object.entries(snapshot)) {
    const normalizedHost = hostName.trim();
    if (!normalizedHost || !Array.isArray(stacks)) continue;
    result[normalizedHost] = stacks;
  }
  return result;
}

export function useRuntimePolling() {
  const connectionRef = useRef<HubConnection | null>(null);

  const loadTags = useCallback(async (agentHostName: string) => {
    const trimmedHost = agentHostName.trim();
    if (!trimmedHost) {
      dispatchApp({
        type: "TAGS_FAILURE",
        hostName: "",
        error: "Не указано имя агента.",
      });
      return;
    }

    dispatchApp({ type: "TAGS_REQUEST", hostName: trimmedHost });

    const connection = connectionRef.current;
    if (!connection || connection.state !== HubConnectionState.Connected) {
      dispatchApp({
        type: "TAGS_FAILURE",
        hostName: trimmedHost,
        error:
          "SignalR (/hubs/ui) не подключён. Обновите страницу или дождитесь соединения.",
      });
      return;
    }

    try {
      const raw = await connection.invoke<Array<{ tag?: string | null }>>(
        "GetHarborTags",
        trimmedHost
      );
      const items = (raw ?? [])
        .map((x) => ({ tag: String(x?.tag ?? "").trim() }))
        .filter((x) => x.tag.length > 0);
      dispatchApp({ type: "TAGS_SUCCESS", hostName: trimmedHost, items });
    } catch (e) {
      dispatchApp({
        type: "TAGS_FAILURE",
        hostName: trimmedHost,
        error: errMessage(e),
      });
    }
  }, []);

  const syncRuntimeSnapshot = useCallback(async (connection: HubConnection) => {
    const snapshot = await connection.invoke<RuntimeSnapshotByHost>(
      "GetRuntimeSnapshot"
    );
    dispatchApp({
      type: "RUNTIME_SNAPSHOT_BY_HOST",
      stacksByHost: mapRuntimeSnapshotByHost(snapshot),
    });
  }, []);

  const loadStatus = useCallback(async () => {
    const connection = connectionRef.current;
    if (!connection || connection.state !== HubConnectionState.Connected) return;
    try {
      await syncRuntimeSnapshot(connection);
    } catch {
      /* ignore runtime sync errors */
    }
  }, [syncRuntimeSnapshot]);

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/ui")
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    connectionRef.current = connection;

    const syncAgentsSnapshot = async () => {
      const snapshot =
        await connection.invoke<Record<string, AgentConnectionInfo>>("GetAgentsSnapshot");
      dispatchApp({ type: "AGENTS_SNAPSHOT", agents: snapshot ?? {} });
    };

    const applyStatusUpdated = (payload: StatusUpdatedEvent) => {
      const parsed = parseStatusUpdatedEvent(payload);
      if (!parsed) return;
      dispatchApp({
        type: "RUNTIME_HOST_UPDATED",
        hostName: parsed.hostName,
        stacks: parsed.stacks,
      });
    };

    connection.on("agent_updated", (payload: AgentUpdatedEvent) => {
      const hostName = payload?.hostName?.trim();
      if (!hostName) return;
      dispatchApp({
        type: "AGENT_UPDATED",
        hostName,
        agent:
          payload.status === null
            ? null
            : {
                status: payload.status ?? "",
                ipAddress: payload.ipAddress ?? null,
                disconnectedAtUtc: payload.disconnectedAtUtc ?? null,
              },
      });
      if (payload.status === AGENT_STATUS_DISCONNECTED) {
        dispatchApp({ type: "RUNTIME_HOST_CLEARED", hostName });
      }
    });

    connection.on("status_updated", applyStatusUpdated);

    connection.onreconnected(async () => {
      try {
        await syncAgentsSnapshot();
        await syncRuntimeSnapshot(connection);
      } catch (e) {
        console.error("[SignalR /hubs/ui] ошибка синхронизации после reconnect:", errMessage(e));
      }
    });

    const startConnection = async () => {
      try {
        await connection.start();
        await syncAgentsSnapshot();
        await syncRuntimeSnapshot(connection);
      } catch (e) {
        console.error(
          "[SignalR /hubs/ui] подключение не удалось — negotiate/WebSocket недоступны. Проверь, что API запущен и Vite-proxy /hubs ведёт на него:",
          errMessage(e)
        );
      }
    };

    void startConnection();

    return () => {
      connectionRef.current = null;
      connection.off("agent_updated");
      connection.off("status_updated");
      void connection.stop();
    };
  }, [syncRuntimeSnapshot]);

  return { loadTags, loadStatus };
}
