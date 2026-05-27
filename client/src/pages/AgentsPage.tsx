import { useMemo } from "react";
import { Link } from "react-router-dom";
import {
  AGENT_STATUS_DISCONNECTED,
  AGENT_STATUS_PASSWORD_ACCEPTED,
  AGENT_STATUS_WAITING_PASSWORD,
} from "../constants";
import { useAppStore } from "../store/appStore";
import type { SslCertificateInfo } from "../types";
import "../components/StackCard/StackCard.css";
import "./AgentsPage.css";

function normalizeIpAddress(ipAddress?: string | null) {
  const raw = ipAddress?.trim();
  if (!raw) return null;
  return raw.startsWith("::ffff:") ? raw.slice("::ffff:".length) : raw;
}

function isReadonlyAgent(type?: string | null) {
  return type?.trim().toLowerCase() === "readonly";
}

function compareVersionTags(a: string, b: string) {
  const aParts = a.split(".").map((part) => Number.parseInt(part, 10));
  const bParts = b.split(".").map((part) => Number.parseInt(part, 10));
  const maxLength = Math.max(aParts.length, bParts.length);

  for (let index = 0; index < maxLength; index += 1) {
    const aValue = Number.isFinite(aParts[index]) ? aParts[index] : 0;
    const bValue = Number.isFinite(bParts[index]) ? bParts[index] : 0;
    if (aValue !== bValue) return bValue - aValue;
  }

  return b.localeCompare(a, undefined, { sensitivity: "base" });
}

function getMostUrgentCertificate(certificates: SslCertificateInfo[]) {
  let result: SslCertificateInfo | null = null;

  for (const certificate of certificates) {
    if (typeof certificate.daysLeft !== "number") continue;
    if (result === null || certificate.daysLeft < (result.daysLeft ?? Number.POSITIVE_INFINITY))
      result = certificate;
  }

  return result;
}

export function AgentsPage() {
  const { status } = useAppStore();

  const entries = useMemo(() => {
    return Object.entries(status.agents).sort(([a], [b]) =>
      a.localeCompare(b, undefined, { sensitivity: "base" })
    );
  }, [status.agents]);

  return (
    <div className="agents-page">
      <h1 className="agents-page__title">Агенты</h1>
      <div className="agents-page__list">
        {entries.length === 0 ? (
          <p className="agents-page__empty">Подключённых агентов нет.</p>
        ) : (
          entries.map(([hostName, agentInfo]) => {
            const agentStatus = agentInfo.status;
            const agentIpAddress = normalizeIpAddress(agentInfo.ipAddress);
            const disconnectedAtText = agentInfo.disconnectedAtUtc
              ? new Date(agentInfo.disconnectedAtUtc).toLocaleString("ru-RU")
              : null;
            const readonlyAgent = isReadonlyAgent(agentInfo.type);
            const versionTags = Array.from(
              new Set(
                (status.stacksByHost[hostName] ?? [])
                  .map((stack) => stack.tag?.trim())
                  .filter((tag): tag is string => Boolean(tag))
              )
            ).sort(compareVersionTags);
            const newestVersion = versionTags[0] ?? null;
            const otherVersions = versionTags.slice(1);
            const versionLine = newestVersion
              ? otherVersions.length > 0
                ? `${newestVersion} (${otherVersions.join(", ")})`
                : newestVersion
              : null;
            const allCertificates = (status.stacksByHost[hostName] ?? []).flatMap(
              (stack) => stack.certificates ?? []
            );
            const mostUrgentCertificate = getMostUrgentCertificate(allCertificates);
            const isWaiting =
              agentStatus === AGENT_STATUS_WAITING_PASSWORD;
            const isPasswordAccepted =
              agentStatus === AGENT_STATUS_PASSWORD_ACCEPTED;
            const isDisconnected =
              agentStatus === AGENT_STATUS_DISCONNECTED;
            const cardClass = isDisconnected
              ? "stack-card is-disconnected agents-page__card--click"
              : isWaiting
                ? "stack-card is-loading agents-page__card--click"
                : isPasswordAccepted
                  ? "stack-card is-running agents-page__card--click"
                  : "stack-card agents-page__card--click";
            const to = `/agents/${encodeURIComponent(hostName)}`;
            return (
              <div key={hostName} className="agents-page__card-wrap">
                <Link to={to} className="agents-page__card-link">
                  <div className={cardClass}>
                    <div className="agents-page__card-head">
                      <span className="agents-page__status">{agentStatus}</span>
                      {readonlyAgent && (
                        <span className="agents-page__meta">readonly</span>
                      )}
                      <span className="stack-card__tag">{hostName}</span>
                    </div>
                    <div className="agents-page__card-main">
                      {agentIpAddress && (
                        <span className="agents-page__meta">
                          IP: <strong>{agentIpAddress}</strong>
                        </span>
                      )}
                      {versionLine && (
                        <span className="agents-page__meta agents-page__meta--version">
                          Версия: <strong>{versionLine}</strong>
                        </span>
                      )}
                      {mostUrgentCertificate && (
                        <span className="agents-page__meta agents-page__meta--ssl">
                          SSL:{" "}
                          <strong>{mostUrgentCertificate.domain}</strong>{" "}
                          <span className="agents-page__ssl-days">
                            ({mostUrgentCertificate.daysLeft} дн.)
                          </span>
                        </span>
                      )}
                    </div>
                    {isDisconnected && disconnectedAtText && (
                      <div className="agents-page__card-extra">
                        <span className="agents-page__meta">
                          Дисконнект: <strong>{disconnectedAtText}</strong>
                        </span>
                      </div>
                    )}
                  </div>
                </Link>
              </div>
            );
          })
        )}
      </div>
    </div>
  );
}
