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

export interface StatusResponse {
  running?: boolean;
  stacks?: StackRuntimeItem[];
  agents?: Record<string, string>;
}

export interface AgentUpdatedEvent {
  hostName?: string;
  status?: string | null;
}

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
