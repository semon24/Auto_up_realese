import type { RegistryChannel } from "../types";

export function buildTagCacheKey(
  agentHostName: string,
  registryChannel?: RegistryChannel
) {
  const hostName = agentHostName.trim();
  return registryChannel ? `${hostName}::${registryChannel}` : hostName;
}
