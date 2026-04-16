export function formatErrorForUi(raw: string, maxChars: number): string {
  const normalized = raw.replace(/\r\n/g, "\n").trim();
  if (normalized.length <= maxChars) return normalized;
  return `...${normalized.slice(-(maxChars - 3))}`;
}
