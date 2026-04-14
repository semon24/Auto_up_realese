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
  activeTag?: string | null;
  serviceLinks?: ServiceLinks | null;
}
