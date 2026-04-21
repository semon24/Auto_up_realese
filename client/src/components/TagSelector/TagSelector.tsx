import { type RefObject, useRef } from "react";
import type { TagItem } from "../../types";
import "./TagSelector.css";

interface TagSelectorProps {
  wrapRef: RefObject<HTMLDivElement>;
  query: string;
  open: boolean;
  filtered: TagItem[];
  effectiveTag: string | null;
  onQueryChange: (value: string) => void;
  onOpen: () => void;
  onPick: (tag: string) => void;
}

export function TagSelector({
  wrapRef,
  query,
  open,
  filtered,
  effectiveTag,
  onQueryChange,
  onOpen,
  onPick,
}: TagSelectorProps) {
  const inputRef = useRef<HTMLInputElement>(null);

  return (
    <div className="tag-selector" ref={wrapRef}>
      <div className="tag-selector__field">
        <input
          ref={inputRef}
          type="text"
          className="tag-selector__input"
          placeholder="Тег образа..."
          value={query}
          onChange={(e) => onQueryChange(e.target.value)}
          onFocus={() => onOpen()}
          autoComplete="off"
        />
      </div>
      {open && filtered.length > 0 && (
        <ul className="tag-selector__dropdown" role="listbox">
          {filtered.map((item) => (
            <li key={item.tag}>
              <button
                type="button"
                className={
                  effectiveTag === item.tag
                    ? "tag-selector__item tag-selector__item--effective"
                    : "tag-selector__item"
                }
                onClick={() => onPick(item.tag)}
              >
                {item.tag}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
