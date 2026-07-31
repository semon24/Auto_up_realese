import {
  flattenStacksByHost,
  isAnyStackRunning,
} from "./utils/runtimeStatus";
import type {
  AgentConnectionInfo,
  RuntimeSnapshotByHost,
  StackRuntimeItem,
  TagItem,
  RegistryChannel,
} from "./types";
import { buildTagCacheKey } from "./utils/registryChannel";

export interface AppState {
  /** Harbor-теги, полученные через конкретного агента (ключ — имя хоста). */
  tagsByHost: Record<string, TagItem[]>;
  /** Нормализованное имя хоста, для которого сейчас идёт загрузка тегов, если есть. */
  tagsLoadingHost: string | null;
  /** Ошибки запроса тегов по хосту. */
  tagsErrorByHost: Record<string, string>;
  /** Сообщение вне привязки к агенту (например, «нужен один агент» на MainPage). */
  tagsClientError: string | null;
  status: {
    running: boolean;
    stacks: StackRuntimeItem[];
    stacksByHost: RuntimeSnapshotByHost;
    agents: Record<string, AgentConnectionInfo>;
  };
  selectedTagView: string | null;
  query: string;
  open: boolean;
  actionError: string | null;
  loading: boolean;
}

export const initialAppState: AppState = {
  tagsByHost: {},
  tagsLoadingHost: null,
  tagsErrorByHost: {},
  tagsClientError: null,
  status: { running: false, stacks: [], stacksByHost: {}, agents: {} },
  selectedTagView: null,
  query: "",
  open: false,
  actionError: null,
  loading: false,
};

export type AppAction =
  | { type: "TAGS_REQUEST"; hostName: string; registryChannel?: RegistryChannel }
  | { type: "TAGS_SUCCESS"; hostName: string; registryChannel?: RegistryChannel; items: TagItem[] }
  /** hostName пустой — положить текст в tagsClientError (валидация UI). */
  | { type: "TAGS_FAILURE"; hostName: string; registryChannel?: RegistryChannel; error: string }
  | {
      type: "STATUS_SUCCESS";
      running: boolean;
      stacks?: StackRuntimeItem[];
      stacksByHost?: RuntimeSnapshotByHost;
      agents?: Record<string, AgentConnectionInfo>;
    }
  | { type: "RUNTIME_HOST_UPDATED"; hostName: string; stacks: StackRuntimeItem[] }
  | { type: "RUNTIME_HOST_CLEARED"; hostName: string }
  | { type: "RUNTIME_SNAPSHOT_BY_HOST"; stacksByHost: RuntimeSnapshotByHost }
  | { type: "AGENTS_SNAPSHOT"; agents: Record<string, AgentConnectionInfo> }
  | {
      type: "AGENT_UPDATED";
      hostName: string;
      agent: AgentConnectionInfo | null;
    }
  | { type: "QUERY_CHANGE"; query: string }
  | { type: "OPEN_SET"; open: boolean }
  | { type: "ACTION_ERROR_SET"; error: string | null }
  | { type: "LOADING_SET"; loading: boolean }
  | { type: "PICK_TAG"; tag: string }
  | { type: "SELECT_TAG_VIEW"; tag: string | null };

export function appReducer(state: AppState, action: AppAction): AppState {
  switch (action.type) {
    case "TAGS_REQUEST": {
      const trimmed = action.hostName.trim();
      const cacheKey = buildTagCacheKey(trimmed, action.registryChannel);
      const nextErrByHost = { ...state.tagsErrorByHost };
      if (trimmed) delete nextErrByHost[cacheKey];
      return {
        ...state,
        tagsLoadingHost: cacheKey,
        tagsClientError: null,
        tagsErrorByHost: nextErrByHost,
      };
    }
    case "TAGS_SUCCESS":
      return {
        ...state,
        tagsByHost: {
          ...state.tagsByHost,
          [buildTagCacheKey(action.hostName, action.registryChannel)]: action.items,
        },
        tagsLoadingHost: null,
      };
    case "TAGS_FAILURE": {
      const trimmed = action.hostName.trim();
      const cacheKey = buildTagCacheKey(trimmed, action.registryChannel);
      if (!trimmed) {
        return {
          ...state,
          tagsLoadingHost: null,
          tagsClientError: action.error,
        };
      }
      return {
        ...state,
        tagsLoadingHost: null,
        tagsErrorByHost: { ...state.tagsErrorByHost, [cacheKey]: action.error },
      };
    }
    case "STATUS_SUCCESS": {
      const stacksByHost = action.stacksByHost ?? state.status.stacksByHost;
      const stacks = action.stacks ?? flattenStacksByHost(stacksByHost);
      return {
        ...state,
        status: {
          running: action.running,
          stacks,
          stacksByHost,
          agents: action.agents ?? state.status.agents,
        },
      };
    }
    case "RUNTIME_SNAPSHOT_BY_HOST": {
      const stacks = flattenStacksByHost(action.stacksByHost);
      return {
        ...state,
        status: {
          ...state.status,
          stacksByHost: action.stacksByHost,
          stacks,
          running: isAnyStackRunning(stacks),
        },
      };
    }
    case "RUNTIME_HOST_UPDATED": {
      const stacksByHost = {
        ...state.status.stacksByHost,
        [action.hostName]: action.stacks,
      };
      const stacks = flattenStacksByHost(stacksByHost);
      return {
        ...state,
        status: {
          ...state.status,
          stacksByHost,
          stacks,
          running: isAnyStackRunning(stacks),
        },
      };
    }
    case "RUNTIME_HOST_CLEARED": {
      const stacksByHost = { ...state.status.stacksByHost };
      delete stacksByHost[action.hostName];
      const stacks = flattenStacksByHost(stacksByHost);
      return {
        ...state,
        status: {
          ...state.status,
          stacksByHost,
          stacks,
          running: isAnyStackRunning(stacks),
        },
      };
    }
    case "AGENTS_SNAPSHOT":
      return {
        ...state,
        status: {
          ...state.status,
          agents: action.agents,
        },
      };
    case "AGENT_UPDATED": {
      const nextAgents = { ...state.status.agents };
      if (action.agent === null) delete nextAgents[action.hostName];
      else nextAgents[action.hostName] = action.agent;
      return {
        ...state,
        status: {
          ...state.status,
          agents: nextAgents,
        },
      };
    }
    case "QUERY_CHANGE":
      return { ...state, query: action.query };
    case "OPEN_SET":
      return { ...state, open: action.open };
    case "ACTION_ERROR_SET":
      return { ...state, actionError: action.error };
    case "LOADING_SET":
      return { ...state, loading: action.loading };
    case "PICK_TAG":
      return { ...state, query: action.tag, open: false };
    case "SELECT_TAG_VIEW":
      return { ...state, selectedTagView: action.tag, open: false };
    default:
      return state;
  }
}
