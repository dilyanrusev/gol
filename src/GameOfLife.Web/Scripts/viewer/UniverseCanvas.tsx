import { useCallback, useEffect, useRef } from "react";
import type { Frame } from "../generated/GameOfLife.Web.Simulation";
import { attachGestures } from "./gestures";

export interface UniverseCanvasProps {
  frame: Frame;
  /** The frame's cells, decoded to packed indices. */
  cells: number[];
  /** Shows the crosshair cursor while this client is editing. */
  editing: boolean;
  onPan(dx: number, dy: number): void;
  onZoom(factor: number): void;
  onRecentre(): void;
  /** A tap on cell (x, y) of the viewport; cellPx says how big the cell was on screen. */
  onCellTap(x: number, y: number, cellPx: number): void;
  /** The canvas's CSS size, whenever it changes; the view's shape follows it. */
  onSizeChange(width: number, height: number): void;
}

/** Where the grid sits inside the canvas (CSS px), so a tap can be mapped back to a cell. */
interface Geometry {
  cellPx: number;
  ox: number;
  oy: number;
}

/**
 * The viewport, drawn on a canvas. Drawing is imperative by nature and happens in an effect; the
 * gesture listeners are attached once and read the latest frame and handlers through refs.
 */
export function UniverseCanvas({ frame, cells, editing, onPan, onZoom, onRecentre, onCellTap, onSizeChange }: UniverseCanvasProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const frameRef = useRef(frame);
  const cellsRef = useRef(cells);
  const handlersRef = useRef({ onPan, onZoom, onRecentre, onCellTap, onSizeChange });
  const geometry = useRef<Geometry>({ cellPx: 1, ox: 0, oy: 0 });
  const lastSize = useRef({ width: 0, height: 0 });

  useEffect(() => {
    handlersRef.current = { onPan, onZoom, onRecentre, onCellTap, onSizeChange };
  }, [onPan, onZoom, onRecentre, onCellTap, onSizeChange]);

  const draw = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d")!;
    const f = frameRef.current;
    const dpr = window.devicePixelRatio || 1;
    const cssWidth = canvas.clientWidth;
    const cssHeight = canvas.clientHeight;
    if (cssWidth !== lastSize.current.width || cssHeight !== lastSize.current.height) {
      lastSize.current = { width: cssWidth, height: cssHeight };
      handlersRef.current.onSizeChange(cssWidth, cssHeight);
    }
    if (canvas.width !== Math.round(cssWidth * dpr) || canvas.height !== Math.round(cssHeight * dpr)) {
      canvas.width = Math.round(cssWidth * dpr);
      canvas.height = Math.round(cssHeight * dpr);
    }
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

    const cellPx = Math.min(cssWidth / f.width, cssHeight / f.height);
    const gridW = cellPx * f.width;
    const gridH = cellPx * f.height;
    const ox = (cssWidth - gridW) / 2;
    const oy = (cssHeight - gridH) / 2;
    geometry.current = { cellPx, ox, oy };
    const style = getComputedStyle(document.documentElement);

    ctx.fillStyle = style.getPropertyValue("--bs-body-bg") || "#fff";
    ctx.fillRect(0, 0, cssWidth, cssHeight);

    ctx.fillStyle = style.getPropertyValue("--bs-primary") || "#0d6efd";
    const inset = cellPx >= 6 ? 1 : 0;
    for (const i of cellsRef.current) {
      const x = i % f.width;
      const y = (i - x) / f.width;
      ctx.fillRect(ox + x * cellPx + inset, oy + y * cellPx + inset, cellPx - inset, cellPx - inset);
    }

    if (cellPx >= 6) {
      ctx.strokeStyle = style.getPropertyValue("--bs-border-color-translucent") || "rgba(0,0,0,.1)";
      ctx.lineWidth = 1;
      ctx.beginPath();
      for (let x = 0; x <= f.width; x++) { ctx.moveTo(ox + x * cellPx + 0.5, oy); ctx.lineTo(ox + x * cellPx + 0.5, oy + gridH); }
      for (let y = 0; y <= f.height; y++) { ctx.moveTo(ox, oy + y * cellPx + 0.5); ctx.lineTo(ox + gridW, oy + y * cellPx + 0.5); }
      ctx.stroke();
    }

    // Outline the viewport so its edges are visible when the grid does not fill the canvas.
    ctx.strokeStyle = style.getPropertyValue("--bs-border-color") || "#dee2e6";
    ctx.strokeRect(ox + 0.5, oy + 0.5, gridW - 1, gridH - 1);
  }, []);

  // Redraw whenever a new frame arrives.
  useEffect(() => {
    frameRef.current = frame;
    cellsRef.current = cells;
    draw();
  }, [frame, cells, draw]);

  // Redraw on resize and theme changes; attach the gestures once. The colours come from Bootstrap's
  // CSS variables, which change with the theme attribute on the root element, not with the OS setting.
  useEffect(() => {
    const canvas = canvasRef.current!;
    const observer = new ResizeObserver(draw);
    observer.observe(canvas);
    const theme = new MutationObserver(draw);
    theme.observe(document.documentElement, { attributes: true, attributeFilter: ["data-bs-theme"] });
    const detach = attachGestures(canvas, {
      pan: (dx, dy) => handlersRef.current.onPan(dx, dy),
      zoom: (factor) => handlersRef.current.onZoom(factor),
      recentre: () => handlersRef.current.onRecentre(),
      cellSize: () => geometry.current.cellPx,
      tap: (px, py) => {
        const { cellPx, ox, oy } = geometry.current;
        const f = frameRef.current;
        const x = Math.floor((px - ox) / cellPx);
        const y = Math.floor((py - oy) / cellPx);
        if (x < 0 || y < 0 || x >= f.width || y >= f.height) return;
        handlersRef.current.onCellTap(x, y, cellPx);
      },
    });
    return () => {
      observer.disconnect();
      theme.disconnect();
      detach();
    };
  }, [draw]);

  return (
    <canvas
      id="universe"
      ref={canvasRef}
      className={`universe-canvas border rounded bg-body-tertiary${editing ? " editing" : ""}`}
      tabIndex={0}
      aria-label="Game of Life universe viewport. Drag or use arrow keys to pan, scroll or pinch to zoom."
    />
  );
}
