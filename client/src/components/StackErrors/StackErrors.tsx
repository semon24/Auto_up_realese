import type { StackRuntimeItem } from "../../types";
import "./StackErrors.css";

interface StackErrorsProps {
  items: StackRuntimeItem[];
  formatError: (raw: string) => string;
}

export function StackErrors({ items, formatError }: StackErrorsProps) {
  if (items.length === 0) return null;

  return (
    <div className="main-errors">
      {items.map((stack, index) => (
        <div key={stack.tag} className="main-errors__item">
          {index > 0 && <hr className="main-errors__divider" />}
          <p className="main-errors__title">Ошибка для тега: {stack.tag}</p>
          <pre className="main-errors__text">
            {formatError(stack.operationError ?? "")}
          </pre>
        </div>
      ))}
    </div>
  );
}
