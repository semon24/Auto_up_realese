import type { StackRuntimeItem } from "../../types";
import { StackCard } from "../StackCard/StackCard";
import "./StackSidebar.css";

interface StackSidebarProps {
  stacks: StackRuntimeItem[];
  selectedTagView: string | null;
  onSelectMain: () => void;
  onSelectStack: (tag: string) => void;
}

export function StackSidebar({
  stacks,
  selectedTagView,
  onSelectMain,
  onSelectStack,
}: StackSidebarProps) {
  return (
    <aside className="stack-sidebar">
      <button
        type="button"
        className={`stack-main-btn${selectedTagView ? "" : " is-active"}`}
        onClick={onSelectMain}
      >
        Main
      </button>
      <div className="stack-list">
        {stacks.map((stack) => (
          <StackCard
            key={stack.tag}
            stack={stack}
            isActive={selectedTagView === stack.tag}
            onClick={onSelectStack}
          />
        ))}
      </div>
    </aside>
  );
}
