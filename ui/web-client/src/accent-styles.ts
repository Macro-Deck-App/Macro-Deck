const SENTINELS = {
  accent: '__MD_ACCENT__',
  hover: '__MD_ACCENT_HOVER__',
  muted: '__MD_ACCENT_MUTED__',
};

const TEMPLATE_ID = 'md-accent-template';

const STYLE_ID = 'md-accent';

export interface AccentColors {
  accent: string;
  hover: string;
  muted: string;
}

export function fillAccentTemplate(template: string, colors: AccentColors): string {
  return template
    .split(SENTINELS.hover).join(colors.hover)
    .split(SENTINELS.muted).join(colors.muted)
    .split(SENTINELS.accent).join(colors.accent);
}

export function applyAccentStyles(doc: Document, colors: AccentColors): void {
  const template = doc.getElementById(TEMPLATE_ID);
  if (template === null) return;

  let style = doc.getElementById(STYLE_ID) as HTMLStyleElement | null;
  if (style === null) {
    style = doc.createElement('style');
    style.id = STYLE_ID;
    // Last in the head, after the stylesheet whose accent rules these override.
    doc.head.appendChild(style);
  }
  const css = fillAccentTemplate(template.textContent || '', colors);
  if (style.textContent !== css) style.textContent = css;
}
