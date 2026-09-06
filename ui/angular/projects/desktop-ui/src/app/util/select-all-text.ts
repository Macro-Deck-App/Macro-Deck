export function selectAllText(field: HTMLInputElement | HTMLTextAreaElement): void {
  field.focus({ preventScroll: true });
  field.select();
  field.setSelectionRange(0, field.value.length);

  if (document.getSelection()?.toString()) {
    return;
  }

  const range = document.createRange();
  range.selectNodeContents(field);
  const selection = document.getSelection();
  selection?.removeAllRanges();
  selection?.addRange(range);
  field.setSelectionRange(0, field.value.length);
}
