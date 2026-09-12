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
  Shape: 'ui.shape',
  Icon: 'ui.icon',
  Grid: 'ui.grid',
  Gauge: 'ui.gauge',
  Toggle: 'ui.toggle',
  Segmented: 'ui.segmented',
  Dial: 'ui.dial',
  Modifier: 'ui.modifier',
} as const;

export const UI_COMPONENTS_WELL_KNOWN: readonly string[] = [
  UiComponents.Stack, UiComponents.Text, UiComponents.Image, UiComponents.RangeBar,
  UiComponents.Slider, UiComponents.Button, UiComponents.Layer, UiComponents.Chart,
  UiComponents.TextField, UiComponents.List, UiComponents.Transform, UiComponents.Shape,
  UiComponents.Icon, UiComponents.Grid, UiComponents.Gauge, UiComponents.Toggle,
  UiComponents.Segmented, UiComponents.Dial, UiComponents.Modifier,
];

export const UiComponentShapes = {
  Rectangle: 'rectangle', RoundedRectangle: 'rounded-rectangle', Circle: 'circle', Capsule: 'capsule',
  Path: 'path',
} as const;

export const UI_COMPONENT_SHAPES_WELL_KNOWN: readonly string[] = Object.values(UiComponentShapes);

// Index i holds the names ui.icon component version i + 1 added. Published names are frozen: append a new
// group for a new name, never edit an existing one (ADR 0084).
export const UI_ICON_VERSIONS: readonly (readonly string[])[] = [
  [
    'action-button-type', 'alert-triangle', 'align-bottom', 'align-center', 'align-left', 'align-middle',
    'align-right', 'align-top', 'arrow-down', 'arrow-left', 'arrow-right', 'arrow-up', 'bell', 'braces-x',
    'bug', 'chart', 'check', 'chevron-right', 'clipboard', 'clock-type', 'code', 'copy', 'crosshair',
    'device-desktop', 'device-floppy', 'device-phone', 'device-tablet', 'disc', 'discord', 'dots-vertical',
    'download', 'external-link', 'file-text', 'folder', 'folder-plus', 'globe', 'grid', 'heart',
    'history-graph-type', 'image', 'info', 'layers', 'list-play', 'lock', 'log-out', 'message-square',
    'minus', 'moon', 'music-note', 'music-player-type', 'pause', 'pencil', 'pin', 'pin-off', 'play', 'plus',
    'power', 'puzzle', 'refresh', 'scissors', 'search', 'settings', 'sidebar', 'sliders', 'star', 'store',
    'sun', 'trash', 'undo', 'unlock', 'upload', 'user', 'weather-type', 'wifi', 'x', 'zap',
  ],
];

export const UI_ICONS_WELL_KNOWN: readonly string[] =
  UI_ICON_VERSIONS.reduce<string[]>((all, group) => all.concat(group), []);

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
