import { dialAngles, dialHands, dialMetrics, dialTicks } from './dial';

describe('clock dial', () => {
  const SIZE = 200;
  const close = (actual: number, expected: number) => expect(actual).toBeCloseTo(expected, 6);

  it('centres the dial in the box it is given', () => {
    const metrics = dialMetrics(300, 200);

    expect(metrics.cx).toBe(150);
    expect(metrics.cy).toBe(100);
  });

  it('scales every length from the smaller side, so the face fits a wide box', () => {
    const wide = dialMetrics(400, 100);
    const square = dialMetrics(100, 100);

    expect(wide.faceRadius).toBe(square.faceRadius);
    expect(wide.faceRadius * 2).toBeLessThanOrEqual(100);
  });

  it('orders the hands by weight: hour thickest, second thinnest', () => {
    const m = dialMetrics(SIZE, SIZE);

    expect(m.hourStroke).toBeGreaterThan(m.minuteStroke);
    expect(m.minuteStroke).toBeGreaterThan(m.secondStroke);
  });

  it('draws twelve marks with every third one longer and brighter', () => {
    const ticks = dialTicks(SIZE, SIZE);

    expect(ticks.length).toBe(12);

    const major = ticks.filter((_, index) => index % 3 === 0);
    const minor = ticks.filter((_, index) => index % 3 !== 0);

    expect(major.length).toBe(4);
    expect(major[0].strokeWidth).toBeGreaterThan(minor[0].strokeWidth);
    expect(major[0].color).not.toBe(minor[0].color);
  });

  it('puts the first mark straight up and the fourth straight right', () => {
    const ticks = dialTicks(SIZE, SIZE);
    const { cx, cy } = dialMetrics(SIZE, SIZE);

    close(ticks[0].x2, cx);
    expect(ticks[0].y2).toBeLessThan(cy);

    close(ticks[3].y2, cy);
    expect(ticks[3].x2).toBeGreaterThan(cx);
  });

  it('points the hands the way a clock face does', () => {
    const angles = dialAngles(3, 0, 0);

    expect(angles.hour).toBe(90);
    expect(angles.minute).toBe(0);
    expect(angles.second).toBe(0);
  });

  it('carries the hour hand along with the minutes instead of jumping', () => {
    // Half past three sits halfway between the 3 and the 4, not on the 3.
    expect(dialAngles(3, 30, 0).hour).toBe(105);
  });

  it('carries the minute hand along with the seconds', () => {
    expect(dialAngles(0, 10, 30).minute).toBe(63);
  });

  it('treats midnight and noon as twelve, not as an extra turn', () => {
    expect(dialAngles(0, 0, 0).hour).toBe(0);
    expect(dialAngles(12, 0, 0).hour).toBe(0);
    expect(dialAngles(24, 0, 0).hour).toBe(0);
  });

  it('draws each hand from behind the hub through the centre to its tip', () => {
    const { cx, cy } = dialMetrics(SIZE, SIZE);
    const hands = dialHands(SIZE, SIZE, 0, 0, 0);

    // Straight up at twelve: the tip is above the centre and the tail below it.
    close(hands.hour.x2, cx);
    expect(hands.hour.y2).toBeLessThan(cy);
    expect(hands.hour.y1).toBeGreaterThan(cy);
  });

  it('makes the minute hand reach further than the hour hand', () => {
    const { cx, cy } = dialMetrics(SIZE, SIZE);
    const hands = dialHands(SIZE, SIZE, 0, 0, 0);
    const reach = (line: { x2: number; y2: number }) =>
      Math.sqrt((line.x2 - cx) ** 2 + (line.y2 - cy) ** 2);

    expect(reach(hands.minute)).toBeGreaterThan(reach(hands.hour));
    expect(reach(hands.second)).toBeGreaterThan(reach(hands.minute));
  });

  it('keeps every hand inside the face', () => {
    const metrics = dialMetrics(SIZE, SIZE);
    const hands = dialHands(SIZE, SIZE, 7, 38, 52);
    const reach = (line: { x2: number; y2: number }) =>
      Math.sqrt((line.x2 - metrics.cx) ** 2 + (line.y2 - metrics.cy) ** 2);

    for (const hand of [hands.hour, hands.minute, hands.second]) {
      expect(reach(hand)).toBeLessThan(metrics.faceRadius);
    }
  });
});
