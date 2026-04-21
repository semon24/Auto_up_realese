export function formatErrorForUi(raw: string, maxChars: number): string {
  const t = raw.trim();
  if (t.length <= maxChars) return t;
  return `${t.slice(0, maxChars)}…`;
}
