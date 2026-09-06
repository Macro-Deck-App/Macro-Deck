export interface WidgetIconRef {
  type: string;
  reference: string;
}

export const ICON_PACK_ICON_TYPE = 'icon-pack';

export function iconPackRef(reference: string): WidgetIconRef {
  return { type: ICON_PACK_ICON_TYPE, reference };
}

export function isIconPackRef(icon: WidgetIconRef | undefined): icon is WidgetIconRef {
  return !!icon && icon.type === ICON_PACK_ICON_TYPE && !!icon.reference;
}

export function iconPackReferenceOf(icon: WidgetIconRef | undefined): string | undefined {
  return isIconPackRef(icon) ? icon.reference : undefined;
}

export function readWidgetIconRef(icon: unknown, legacyIconId: string | undefined): WidgetIconRef | undefined {
  if (
    icon && typeof icon === 'object' &&
    typeof (icon as WidgetIconRef).type === 'string' && (icon as WidgetIconRef).type &&
    typeof (icon as WidgetIconRef).reference === 'string' && (icon as WidgetIconRef).reference
  ) {
    return { type: (icon as WidgetIconRef).type, reference: (icon as WidgetIconRef).reference };
  }

  const trimmed = legacyIconId?.trim();
  return trimmed ? iconPackRef(trimmed) : undefined;
}
