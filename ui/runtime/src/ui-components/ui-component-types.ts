export const UiComponents = {
  Stack: 'ui.stack',
  Text: 'ui.text',
  Image: 'ui.image',
  RangeBar: 'ui.range-bar',
  Slider: 'ui.slider',
  Button: 'ui.button',
  Layer: 'ui.layer',
  Chart: 'ui.chart',
  TextField: 'ui.text-field',
  List: 'ui.list',
  Transform: 'ui.transform',
} as const;

export const UI_COMPONENTS_WELL_KNOWN: readonly string[] = [
  UiComponents.Stack, UiComponents.Text, UiComponents.Image, UiComponents.RangeBar,
  UiComponents.Slider, UiComponents.Button, UiComponents.Layer, UiComponents.Chart,
  UiComponents.TextField, UiComponents.List, UiComponents.Transform,
];

export const UiComponentDirections = { Vertical: 'vertical', Horizontal: 'horizontal' } as const;

export const UiComponentJustify = {
  Start: 'start', Center: 'center', End: 'end', SpaceBetween: 'space-between',
} as const;

export const UiComponentAlignments = {
  Start: 'start', Center: 'center', End: 'end', Stretch: 'stretch', Baseline: 'baseline',
} as const;

export const UiComponentTextRoles = {
  Primary: 'primary', Secondary: 'secondary', Muted: 'muted',
} as const;

export const UiComponentTextWeights = {
  Regular: 'regular', Medium: 'medium', SemiBold: 'semibold', Bold: 'bold',
} as const;

export const UiComponentImageFits = { Contain: 'contain', Cover: 'cover' } as const;

export const UiComponentImageTransitions = { Crossfade: 'crossfade' } as const;

export const UI_COMPONENT_IMAGE_TRANSITIONS_WELL_KNOWN: readonly string[] =
  Object.values(UiComponentImageTransitions);

export const UiComponentButtonCorners = { Tile: 'tile' } as const;

export const UI_COMPONENT_BUTTON_CORNERS_WELL_KNOWN: readonly string[] =
  Object.values(UiComponentButtonCorners);

export const UiComponentBorderStyles = {
  Static: 'static', Heartbeat: 'heartbeat', Breathing: 'breathing', Blink: 'blink',
  Comet: 'comet', Ants: 'ants', HueShift: 'hue-shift', Rgb: 'rgb',
} as const;

export const UI_COMPONENT_BORDER_STYLES_WELL_KNOWN: readonly string[] =
  Object.values(UiComponentBorderStyles);
