import type { ServiceLinks as ServiceLinksType } from "../../types";
import "./ServiceLinks.css";

interface ServiceLinksProps {
  links: ServiceLinksType;
}

export function ServiceLinks({ links }: ServiceLinksProps) {
  return (
    <div className="service-links">
      <p className="service-links__title">Сервисы</p>
      <ul className="service-links__list">
        {links.admin && (
          <li>
            <a href={links.admin} target="_blank" rel="noopener noreferrer">
              Admin
            </a>
          </li>
        )}
        {links.server && (
          <li>
            <a href={links.server} target="_blank" rel="noopener noreferrer">
              Server
            </a>
          </li>
        )}
        {links.call && (
          <li>
            <a href={links.call} target="_blank" rel="noopener noreferrer">
              Call
            </a>
          </li>
        )}
        {links.portal && (
          <li>
            <a href={links.portal} target="_blank" rel="noopener noreferrer">
              Portal
            </a>
          </li>
        )}
      </ul>
    </div>
  );
}
