import type { BranchItem, ServiceLinks } from "./types";

export interface AppState {
  items: BranchItem[];
  branchError: string | null;
  branchesLoading: boolean;
  status: {
    running: boolean;
    activeTag: string | null;
    serviceLinks: ServiceLinks | null;
  };
  query: string;
  open: boolean;
  actionError: string | null;
  loading: boolean;
}

export const initialAppState: AppState = {
  items: [],
  branchError: null,
  branchesLoading: false,
  status: { running: false, activeTag: null, serviceLinks: null },
  query: "",
  open: false,
  actionError: null,
  loading: false,
};

export type AppAction =
  | { type: "BRANCHES_REQUEST" }
  | { type: "BRANCHES_SUCCESS"; items: BranchItem[] }
  | { type: "BRANCHES_FAILURE"; error: string }
  | {
      type: "STATUS_SUCCESS";
      running: boolean;
      activeTag: string | null;
      serviceLinks?: ServiceLinks | null;
    }
  | { type: "QUERY_CHANGE"; query: string }
  | { type: "OPEN_SET"; open: boolean }
  | { type: "ACTION_ERROR_SET"; error: string | null }
  | { type: "LOADING_SET"; loading: boolean }
  | { type: "PICK_TAG"; tag: string };

export function appReducer(state: AppState, action: AppAction): AppState {
  switch (action.type) {
    case "BRANCHES_REQUEST":
      return { ...state, branchError: null, branchesLoading: true };
    case "BRANCHES_SUCCESS":
      return {
        ...state,
        items: action.items,
        branchError: null,
        branchesLoading: false,
      };
    case "BRANCHES_FAILURE":
      return { ...state, branchError: action.error, branchesLoading: false };
    case "STATUS_SUCCESS":
      return {
        ...state,
        status: {
          running: action.running,
          activeTag: action.activeTag,
          serviceLinks: action.serviceLinks ?? null,
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
    default:
      return state;
  }
}
