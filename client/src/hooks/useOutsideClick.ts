import { useEffect, type RefObject } from "react";

export function useOutsideClick<T extends HTMLElement>(
  ref: RefObject<T>,
  onOutside: () => void
) {
  useEffect(() => {
    function onDoc(e: MouseEvent) {
      const target = e.target;
      if (ref.current && target instanceof Node && !ref.current.contains(target)) {
        onOutside();
      }
    }

    document.addEventListener("mousedown", onDoc);
    return () => document.removeEventListener("mousedown", onDoc);
  }, [ref, onOutside]);
}
