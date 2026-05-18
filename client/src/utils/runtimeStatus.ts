import type { RuntimeServiceState, StackRuntimeItem, StatusUpdatedEvent } from "../types";

type JsonRecord = Record<string, unknown>;

function isRecord(value: unknown): value is JsonRecord {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function readString(obj: JsonRecord, ...keys: string[]): string | null {
  for (const key of keys) {
    const value = obj[key];
    if (typeof value === "string") {
      const trimmed = value.trim();
      if (trimmed.length > 0) return trimmed;
    }
  }
  return null;
}

function readBoolean(obj: JsonRecord, key: string): boolean {
  const value = obj[key];
  if (value === true) return true;
  if (typeof value === "string") return value.toLowerCase() === "true";
  return false;
}

function parseServiceLinks(stack: JsonRecord): StackRuntimeItem["serviceLinks"] {
  const links = stack.serviceLinks ?? stack.ServiceLinks;
  if (!isRecord(links)) return null;

  const result: NonNullable<StackRuntimeItem["serviceLinks"]> = {};
  for (const [name, value] of Object.entries(links)) {
    if (typeof value !== "string") continue;
    const trimmed = value.trim();
    if (trimmed.length > 0) result[name as keyof typeof result] = trimmed;
  }
  return Object.keys(result).length > 0 ? result : null;
}

function parseServices(stack: JsonRecord): Record<string, RuntimeServiceState> {
  const services = stack.services ?? stack.Services;
  if (!isRecord(services)) return {};

  const result: Record<string, RuntimeServiceState> = {};
  for (const [serviceName, rawService] of Object.entries(services)) {
    if (!isRecord(rawService)) continue;
    const state =
      readString(rawService, "state", "State") ?? "unknown";
    const health = readString(rawService, "health", "Health");
    result[serviceName] = { state, health };
  }
  return result;
}

function parseStackItem(tag: string, stack: JsonRecord): StackRuntimeItem {
  const services = parseServices(stack);
  const runningFromServices = Object.values(services).some(
    (service) => service.state.toLowerCase() === "running"
  );
  const running = readBoolean(stack, "running") || runningFromServices;

  return {
    tag,
    running,
    operationType: readString(stack, "operationType", "OperationType"),
    operationStatus: readString(stack, "operationStatus", "OperationStatus"),
    operationError: readString(stack, "operationError", "OperationError"),
    serviceLinks: parseServiceLinks(stack),
    services,
  };
}

/** Парсит payload snapshot от агента (поле stacks/stack). */
export function parseStacksFromSnapshot(snapshot: unknown): StackRuntimeItem[] {
  if (!isRecord(snapshot)) return [];

  const stacksRoot = snapshot.stacks ?? snapshot.stack ?? snapshot.Stacks;
  if (!isRecord(stacksRoot)) return [];

  const items: StackRuntimeItem[] = [];
  for (const [tag, rawStack] of Object.entries(stacksRoot)) {
    const normalizedTag = tag.trim();
    if (!normalizedTag || !isRecord(rawStack)) continue;
    items.push(parseStackItem(normalizedTag, rawStack));
  }
  return items;
}

export function parseStatusUpdatedEvent(
  payload: StatusUpdatedEvent
): { hostName: string; stacks: StackRuntimeItem[] } | null {
  const hostName = payload.hostName?.trim();
  if (!hostName) return null;
  return {
    hostName,
    stacks: parseStacksFromSnapshot(payload.snapshot),
  };
}

export function flattenStacksByHost(
  stacksByHost: Record<string, StackRuntimeItem[]>
): StackRuntimeItem[] {
  const byTag = new Map<string, StackRuntimeItem>();
  for (const stacks of Object.values(stacksByHost)) {
    for (const stack of stacks) byTag.set(stack.tag, stack);
  }
  return [...byTag.values()];
}

export function isAnyStackRunning(stacks: StackRuntimeItem[]): boolean {
  return stacks.some((stack) => stack.running);
}
