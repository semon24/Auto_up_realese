export interface TagItem {
  tag: string;
}

export interface TagsResponse {
  items?: TagItem[];
}

export interface ServiceLinks {
  admin?: string | null;
  server?: string | null;
  portal?: string | null;
  call?: string | null;
}

export interface AgentConnectionInfo {
  status: string;
  ipAddress?: string | null;
  disconnectedAtUtc?: string | null;
}

export interface StatusResponse {
  running?: boolean;
  stacks?: StackRuntimeItem[];
  agents?: Record<string, AgentConnectionInfo>;
}

export interface AgentUpdatedEvent {
  hostName?: string;
  status?: string | null;
  ipAddress?: string | null;
  disconnectedAtUtc?: string | null;
}

export interface StatusUpdatedEvent {
  hostName?: string;
  stacksCount?: number;
  snapshot?: unknown;
}

export type RuntimeSnapshotByHost = Record<string, StackRuntimeItem[]>;

export interface RuntimeServiceState {
  state: string;
  health?: string | null;
}

export interface StackRuntimeItem {
  tag: string;
  running: boolean;
  operationType?: string | null;
  operationStatus?: string | null;
  operationError?: string | null;
  serviceLinks?: ServiceLinks | null;
  services?: Record<string, RuntimeServiceState>;
}
