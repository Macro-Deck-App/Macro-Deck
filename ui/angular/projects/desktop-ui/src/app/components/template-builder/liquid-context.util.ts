export function isInsideLiquidTag(value: string, position: number): boolean {
  const before = value.slice(0, Math.max(0, Math.min(position, value.length)));
  const opened = Math.max(before.lastIndexOf('{{'), before.lastIndexOf('{%'));
  if (opened === -1) {
    return false;
  }

  const closed = Math.max(before.lastIndexOf('}}'), before.lastIndexOf('%}'));
  return closed < opened;
}
