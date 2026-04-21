import type { StackRuntimeItem } from "../../types";
import "./StackErrors.css";

interface StackErrorsProps {
  items: StackRuntimeItem[];
  formatError: (raw: string) => string;
}

export function StackErrors({ items, formatError }: StackErrorsProps) {
  return (
    <div className="stack-errors">
      {items.map((stack) => (
        <div key={stack.tag} className="stack-errors__item">
          <strong className="stack-errors__tag">{stack.tag}</strong>
          <pre className="stack-errors__pre">
            {formatError(stack.operationError ?? "")}
          </pre>
        </div>
      ))}
    </div>
  );
}
