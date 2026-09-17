/** Well-known patterns offered as one-click starting points in the pattern editor. */
export const PRESETS: ReadonlyArray<{ key: string; label: string; rle: string }> = [
  { key: "glider", label: "Glider", rle: "x = 3, y = 3\nbob$2bo$3o!" },
  { key: "gun", label: "Glider gun", rle: "x = 36, y = 9\n24bo$22bobo$12b2o6b2o12b2o$11bo3bo4b2o12b2o$2o8bo5bo3b2o$2o8bo3bob2o4bobo$10bo5bo7bo$11bo3bo$12b2o!" },
  { key: "pulsar", label: "Pulsar", rle: "x = 13, y = 13\n2b3o3b3o2b2$o4bobo4bo$o4bobo4bo$o4bobo4bo$2b3o3b3o2b2$2b3o3b3o2b$o4bobo4bo$o4bobo4bo$o4bobo4bo2$2b3o3b3o!" },
  { key: "rpentomino", label: "R-pentomino", rle: "x = 3, y = 3\nb2o$2o$bo!" },
  { key: "acorn", label: "Acorn", rle: "x = 7, y = 3\nbo5b$3bo3b$2o2b3o!" },
];
