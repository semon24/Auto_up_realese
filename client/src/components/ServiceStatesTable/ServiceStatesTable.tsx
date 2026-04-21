import type { RuntimeServiceState } from "../../types";
import "./ServiceStatesTable.css";

interface ServiceStatesTableProps {
  items: [string, RuntimeServiceState][];
}

export function ServiceStatesTable({ items }: ServiceStatesTableProps) {
  return (
    <div className="service-states">
      <h3 className="service-states__title">Сервисы</h3>
      <table className="service-states__table">
        <thead>
          <tr>
            <th>Сервис</th>
            <th>Состояние</th>
            <th>Health</th>
          </tr>
        </thead>
        <tbody>
          {items.map(([name, s]) => (
            <tr key={name}>
              <td className="service-states__name">{name}</td>
              <td>{s.state}</td>
              <td>{s.health ?? "—"}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
