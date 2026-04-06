export interface BranchItem {
  branch: string;
  tag: string;
}

export interface BranchesResponse {
  items?: BranchItem[];
}

export interface StatusResponse {
  running?: boolean;
  activeTag?: string | null;
}
