export function offersPasteMenu(event: MouseEvent): boolean {
  const target = event.target as HTMLElement | null;
  return !target?.closest('.action-card, input, textarea, select, [contenteditable="true"]');
}
