export interface BranchItem {
  branch: string;
  tag: string;
}

export interface BranchesResponse {
  items?: BranchItem[];
}

export interface ServiceLinks {
  admin?: string | null;
  server?: string | null;
  portal?: string | null;
  call?: string | null;
}

export interface StatusResponse {
  running?: boolean;
  activeTag?: string | null;
  serviceLinks?: ServiceLinks | null;
}
