import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";

type Placement = {
  left: number;
  top: number;
};

export default function ActionDropdownPortal({
  open,
  anchorEl,
  onClose,
  className = "action-dropdown",
  children,
}: {
  open: boolean;
  anchorEl: HTMLElement | null;
  onClose: () => void;
  className?: string;
  children: React.ReactNode;
}) {
  const menuRef = useRef<HTMLDivElement | null>(null);
  const [placement, setPlacement] = useState<Placement | null>(null);

  const computePlacement = useCallback(() => {
    if (!anchorEl) return;
    const rect = anchorEl.getBoundingClientRect();
    const menu = menuRef.current;
    const menuW = menu?.offsetWidth ?? 0;
    const menuH = menu?.offsetHeight ?? 0;

    const margin = 8;
    const left = Math.max(margin, Math.min(rect.right - menuW, window.innerWidth - margin - menuW));
    const below = rect.bottom + 4;
    const preferredTop = below + menuH <= window.innerHeight - margin ? below : rect.top - 4 - menuH;
    const top = Math.max(margin, Math.min(preferredTop, window.innerHeight - margin - menuH));

    setPlacement({ left, top });
  }, [anchorEl]);

  useLayoutEffect(() => {
    if (!open) return;
    setPlacement(null);
    computePlacement();
    const raf = window.requestAnimationFrame(computePlacement);
    return () => window.cancelAnimationFrame(raf);
  }, [open, anchorEl, computePlacement]);

  useEffect(() => {
    if (!open) return;

    const onPointerDown = (e: PointerEvent) => {
      const target = e.target as Node | null;
      if (!target) return;
      if (menuRef.current?.contains(target)) return;
      if (anchorEl?.contains(target)) return;
      onClose();
    };

    const onReflow = () => computePlacement();

    window.addEventListener("pointerdown", onPointerDown);
    window.addEventListener("resize", onReflow);
    window.addEventListener("scroll", onReflow, true);

    return () => {
      window.removeEventListener("pointerdown", onPointerDown);
      window.removeEventListener("resize", onReflow);
      window.removeEventListener("scroll", onReflow, true);
    };
  }, [open, anchorEl, onClose, computePlacement]);

  if (!open || !anchorEl) return null;

  const body = typeof document !== "undefined" ? document.body : null;
  if (!body) return null;

  const style = placement
    ? ({
        position: "fixed",
        left: placement.left,
        top: placement.top,
        zIndex: 1000,
      } as const)
    : ({
        position: "fixed",
        left: -9999,
        top: -9999,
        zIndex: 1000,
      } as const);

  return createPortal(
    <div
      ref={menuRef}
      className={className}
      style={{
        ...style,
        // The TRS stylesheet's right: 0 is for in-flow menus, not viewport portals.
        right: "auto",
        bottom: "auto",
        marginTop: 0,
        width: "max-content",
        minWidth: "min(12rem, calc(100vw - 16px))",
        maxWidth: "calc(100vw - 16px)",
        maxHeight: "calc(100vh - 16px)",
        overflowY: "auto",
        overflowWrap: "anywhere",
        visibility: placement ? "visible" : "hidden",
      }}
      role="menu"
    >
      {children}
    </div>,
    body,
  );
}
