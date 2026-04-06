interface ApiErrorBody {
  error?: string;
}

export function errMessage(e: unknown): string {
  if (e instanceof Error) return e.message;
  return String(e);
}

export async function api<T>(
  path: string,
  options?: RequestInit
): Promise<T> {
  const res = await fetch(path, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...(options?.headers ?? {}),
    },
  });
  const data = (await res.json().catch(() => ({}))) as T & ApiErrorBody;
  if (!res.ok) {
    throw new Error(data.error || res.statusText || "Ошибка запроса");
  }
  return data as T;
}
