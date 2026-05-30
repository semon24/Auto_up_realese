import type { StackRuntimeItem } from "../../types";
import "./StackCard.css";

interface StackCardProps {
  stack: StackRuntimeItem;
  isActive: boolean;
  onClick: (tag: string) => void;
}

export function StackCard({ stack, isActive, onClick }: StackCardProps) {
  const isLoading =
    stack.operationStatus === "in_progress" ||
    stack.operationStatus === "deleting";
  const version = stack.version?.trim();
  const cardClass = isLoading
    ? "stack-card is-loading"
    : stack.running
      ? "stack-card is-running"
      : "stack-card";

  return (
    <div className="stack-card-wrap">
      <button
        type="button"
        className={`${cardClass}${isActive ? " is-active" : ""}`}
        onClick={() => onClick(stack.tag)}
      >
        <span className="stack-card__main">
          <span className="stack-card__tag">{stack.tag}</span>
          {version ? (
            <span className="stack-card__version">{version}</span>
          ) : null}
        </span>
        <span className="stack-card__state">
          {isLoading ? "loading" : stack.running ? "running" : "idle"}
        </span>
      </button>
    </div>
  );
}
