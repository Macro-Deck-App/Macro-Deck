export const UiComponentModifiers = {
  Background: 'background',
  Radius: 'radius',
  BorderWidth: 'borderWidth',
  BorderColor: 'borderColor',
  BorderLine: 'borderLine',
  AccessibilityLabel: 'accessibilityLabel',
  AccessibilityHint: 'accessibilityHint',
  Disabled: 'disabled',
} as const;

export const UI_COMPONENT_MODIFIERS_WELL_KNOWN: readonly string[] = Object.values(UiComponentModifiers);

export const UiComponentClips = { Bounds: 'bounds', Circle: 'circle', Capsule: 'capsule' } as const;

export const UI_COMPONENT_CLIPS_WELL_KNOWN: readonly string[] = Object.values(UiComponentClips);

export const UiComponentBorderLines = { Solid: 'solid', Dashed: 'dashed', Dotted: 'dotted' } as const;

export const UI_COMPONENT_BORDER_LINES_WELL_KNOWN: readonly string[] = Object.values(UiComponentBorderLines);

export const UI_MODIFIER_DIM_OPACITY = 0.4;

export const UI_GESTURE_SLOP = 0.04;

export const UI_SWIPE_MIN_DISTANCE = 0.2;

export const UI_SWIPE_MAX_DURATION_MS = 500;

export const UI_GESTURE_THROTTLE_MS = 100;
