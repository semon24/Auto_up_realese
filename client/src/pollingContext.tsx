import { createContext, useContext, type ReactNode } from "react";

export interface PollingApi {
  loadTags: (agentHostName: string) => void;
  loadStatus: () => Promise<void>;
}

const PollingContext = createContext<PollingApi | null>(null);

export function PollingProvider({
  value,
  children,
}: {
  value: PollingApi;
  children: ReactNode;
}) {
  return (
    <PollingContext.Provider value={value}>{children}</PollingContext.Provider>
  );
}

export function usePolling(): PollingApi {
  const ctx = useContext(PollingContext);
  if (!ctx)
    throw new Error("usePolling должен вызываться внутри PollingProvider");
  return ctx;
}
