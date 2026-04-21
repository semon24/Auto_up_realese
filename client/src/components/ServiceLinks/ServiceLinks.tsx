import type { ServiceLinks as ServiceLinksType } from "../../types";
import "./ServiceLinks.css";

interface ServiceLinksProps {
  links: ServiceLinksType;
}

export function ServiceLinks({ links }: ServiceLinksProps) {
  const entries = (
    [
      ["admin", links.admin],
      ["server", links.server],
      ["portal", links.portal],
      ["call", links.call],
    ] as const
  ).filter(([, url]) => url && url.trim().length > 0) as [string, string][];

  return (
    <div className="service-links">
      <h3 className="service-links__title">Ссылки</h3>
      <ul className="service-links__list">
        {entries.map(([label, url]) => (
          <li key={label}>
            <a href={url} target="_blank" rel="noreferrer">
              {label}: {url}
            </a>
          </li>
        ))}
      </ul>
    </div>
  );
}
