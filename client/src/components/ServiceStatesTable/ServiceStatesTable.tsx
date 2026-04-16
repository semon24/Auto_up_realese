import type { RuntimeServiceState } from "../../types";
import "./ServiceStatesTable.css";

interface ServiceStatesTableProps {
  items: Array<[string, RuntimeServiceState]>;
}

export function ServiceStatesTable({ items }: ServiceStatesTableProps) {
  if (items.length === 0) return null;

  return (
    <div className="service-states">
      <p className="service-states__title">Состояние сервисов</p>
      <table className="service-states__table">
        <thead>
          <tr>
            <th>Название</th>
            <th>State</th>
            <th>Health</th>
          </tr>
        </thead>
        <tbody>
          {items.map(([serviceName, service]) => (
            <tr key={serviceName}>
              <td className="service-states__name">{serviceName}</td>
              <td>{service.state}</td>
              <td>{service.health ?? "-"}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
