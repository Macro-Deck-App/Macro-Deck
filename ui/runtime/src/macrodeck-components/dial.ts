export interface UiDialLine {
  x1: number;
  y1: number;
  x2: number;
  y2: number;
}

export interface UiDialTick extends UiDialLine {
  strokeWidth: number;
  color: string;
}

export interface UiDialMetrics {
  cx: number;
  cy: number;
  faceRadius: number;
  hubRadius: number;
  hourStroke: number;
  minuteStroke: number;
  secondStroke: number;
}

export function dialMetrics(width: number, height: number): UiDialMetrics {
  const d = Math.min(width, height);
  return {
    cx: width / 2,
    cy: height / 2,
    faceRadius: d * 0.48,
    hubRadius: d * 0.026,
    hourStroke: d * 0.04,
    minuteStroke: d * 0.025,
    secondStroke: d * 0.0125,
  };
}

export function dialTicks(width: number, height: number): UiDialTick[] {
  const d = Math.min(width, height);
  const { cx, cy } = dialMetrics(width, height);
  const ticks: UiDialTick[] = [];

  for (let index = 0; index < 12; index++) {
    const major = index % 3 === 0;
    const angle = (index * Math.PI) / 6;
    const inner = d * (major ? 0.37 : 0.405);
    const outer = d * 0.44;
    const sin = Math.sin(angle);
    const cos = Math.cos(angle);
    ticks.push({
      x1: cx + inner * sin,
      y1: cy - inner * cos,
      x2: cx + outer * sin,
      y2: cy - outer * cos,
      strokeWidth: d * (major ? 0.025 : 0.015),
      color: major ? 'var(--color-text-secondary)' : 'var(--color-text-muted)',
    });
  }

  return ticks;
}

export function dialAngles(hours: number, minutes: number, seconds: number): {
  hour: number;
  minute: number;
  second: number;
} {
  return {
    hour: (hours % 12) * 30 + minutes * 0.5,
    minute: minutes * 6 + seconds * 0.1,
    second: seconds * 6,
  };
}

function handLine(
  width: number,
  height: number,
  angleDeg: number,
  tailFraction: number,
  tipFraction: number,
): UiDialLine {
  const d = Math.min(width, height);
  const { cx, cy } = dialMetrics(width, height);
  const angle = (angleDeg * Math.PI) / 180;
  const sin = Math.sin(angle);
  const cos = Math.cos(angle);
  const tail = d * tailFraction;
  const tip = d * tipFraction;
  return { x1: cx - tail * sin, y1: cy + tail * cos, x2: cx + tip * sin, y2: cy - tip * cos };
}

export function dialHands(width: number, height: number, hours: number, minutes: number, seconds: number): {
  hour: UiDialLine;
  minute: UiDialLine;
  second: UiDialLine;
} {
  const angles = dialAngles(hours, minutes, seconds);
  return {
    hour: handLine(width, height, angles.hour, 0.05, 0.21),
    minute: handLine(width, height, angles.minute, 0.05, 0.33),
    second: handLine(width, height, angles.second, 0.08, 0.37),
  };
}
