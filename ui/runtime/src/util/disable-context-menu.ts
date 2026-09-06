// A widget answers long press itself, and Android/desktop raise their context menu from the same
// gesture. iOS raises a callout no listener sees - -webkit-touch-callout in the stylesheets stops
// that one. Scoped to an element, and text entries keep their menu: it is how a pointer-only user
// pastes. The packaged desktop app suppresses the menu at the webview level instead.
export function disableContextMenu(target: EventTarget): void {
  target.addEventListener('contextmenu', (event: Event) => {
    if (isTextEntry(event.target)) return;
    event.preventDefault();
  });
}

function isTextEntry(node: EventTarget | null): boolean {
  const element = node as { tagName?: string; isContentEditable?: boolean } | null;
  if (!element || typeof element.tagName !== 'string') return false;
  const tag = element.tagName.toLowerCase();
  return tag === 'input' || tag === 'textarea' || element.isContentEditable === true;
}
