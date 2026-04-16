import type { RefObject } from "react";
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
  return (
    <div className="combo-wrap" ref={wrapRef}>
      <input
        className="search"
        type="search"
        placeholder="Поиск по тегу…"
        value={query}
        onChange={(e) => onQueryChange(e.target.value)}
        onFocus={onOpen}
        autoComplete="off"
        aria-expanded={open}
        aria-controls="tag-listbox"
      />
      {open && filtered.length > 0 && (
        <ul id="tag-listbox" className="list" role="listbox">
          {filtered.map((x) => (
            <li
              key={x.tag}
              role="option"
              aria-selected={effectiveTag === x.tag}
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => onPick(x.tag)}
            >
              {x.tag}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
