import { create } from "zustand";
import { devtools } from "zustand/middleware";
import {
  appReducer,
  initialAppState,
  type AppAction,
  type AppState,
} from "../appReducer";

export const useAppStore = create<AppState>()(
  devtools(() => ({ ...initialAppState }), {
    name: "AutoUpRelease",
    enabled: import.meta.env.DEV,
  })
);

export function dispatchApp(action: AppAction) {
  useAppStore.setState(
    (state) => appReducer(state, action),
    false,
    action.type
  );
}
