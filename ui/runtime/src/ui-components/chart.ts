export interface UiChartPaths {
  area: string;
  line: string;
}

function round2(value: number): number {
  return Math.round(value * 100) / 100;
}

export function chartPaths(
  points: readonly number[],
  width: number,
  height: number,
  plotTop: number,
): UiChartPaths | null {
  if (points.length === 0) return null;

  const right = round2(width);
  const bottom = round2(height);
  const top = plotTop * bottom;
  const y = (value: number) => round2(bottom - value * (bottom - top));

  const run = points.length === 1
    ? `0 ${y(points[0])} L${right} ${y(points[0])}`
    : points
      .map((value, index) => {
        const x = round2((index / (points.length - 1)) * right);
        return index === 0 ? `${x} ${y(value)}` : ` L${x} ${y(value)}`;
      })
      .join('');

  return { line: `M${run}`, area: `M0 ${bottom} L${run} L${right} ${bottom} Z` };
}
