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
  type?: string | null;
  mode?: string | null;
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
  type?: string | null;
  mode?: string | null;
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

export interface SslCertificateInfo {
  domain: string;
  notBeforeUtc?: string | null;
  notAfterUtc?: string | null;
  daysLeft?: number | null;
  isValid: boolean;
  error?: string | null;
}

export interface StackRuntimeItem {
  hostName?: string;
  tag: string;
  stackName?: string | null;
  version?: string | null;
  running: boolean;
  operationType?: string | null;
  operationStatus?: string | null;
  operationError?: string | null;
  serviceLinks?: ServiceLinks | null;
  serviceDomains?: string[] | null;
  services?: Record<string, RuntimeServiceState>;
  certificates?: SslCertificateInfo[] | null;
}
