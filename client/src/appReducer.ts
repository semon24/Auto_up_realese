import type { StackRuntimeItem, TagItem } from "./types";

export interface AppState {
  items: TagItem[];
  tagsError: string | null;
  tagsLoading: boolean;
  status: {
    running: boolean;
    stacks: StackRuntimeItem[];
    agents: Record<string, string>;
  };
  selectedTagView: string | null;
  query: string;
  open: boolean;
  actionError: string | null;
  loading: boolean;
}

export const initialAppState: AppState = {
  items: [],
  tagsError: null,
  tagsLoading: false,
  status: { running: false, stacks: [], agents: {} },
  selectedTagView: null,
  query: "",
  open: false,
  actionError: null,
  loading: false,
};

export type AppAction =
  | { type: "TAGS_REQUEST" }
  | { type: "TAGS_SUCCESS"; items: TagItem[] }
  | { type: "TAGS_FAILURE"; error: string }
  | {
      type: "STATUS_SUCCESS";
      running: boolean;
      stacks?: StackRuntimeItem[];
      agents?: Record<string, string>;
    }
  | { type: "QUERY_CHANGE"; query: string }
  | { type: "OPEN_SET"; open: boolean }
  | { type: "ACTION_ERROR_SET"; error: string | null }
  | { type: "LOADING_SET"; loading: boolean }
  | { type: "PICK_TAG"; tag: string }
  | { type: "SELECT_TAG_VIEW"; tag: string | null };

export function appReducer(state: AppState, action: AppAction): AppState {
  switch (action.type) {
    case "TAGS_REQUEST":
      return { ...state, tagsError: null, tagsLoading: true };
    case "TAGS_SUCCESS":
      return {
        ...state,
        items: action.items,
        tagsError: null,
        tagsLoading: false,
      };
    case "TAGS_FAILURE":
      return { ...state, tagsError: action.error, tagsLoading: false };
    case "STATUS_SUCCESS":
      return {
        ...state,
        status: {
          running: action.running,
          stacks: action.stacks ?? [],
          agents: action.agents ?? {},
        },
      };
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
