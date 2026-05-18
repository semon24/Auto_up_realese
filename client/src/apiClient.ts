interface ApiErrorBody {
  error?: string;
}

export function errMessage(e: unknown): string {
  if (e instanceof Error) {
    let msg = e.message;
    const failedInvoke =
      msg.includes("'GetHarborTags'") && /Failed to invoke/i.test(msg);
    if (failedInvoke) {
      msg =
        `${msg} Проверьте совпадение имени хоста в UI с параметром подключения агента и логи API (ответ агента TagsUpdated/Harbor).`;
    }
    return msg;
  }
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
