export const UiMacroDeckComponents = {
  DynamicText: 'macrodeck.dynamic-text',
  ClockDial: 'macrodeck.clock-dial',
  ProgressBar: 'macrodeck.progress-bar',
  ProgressText: 'macrodeck.progress-text',
  VideoStream: 'macrodeck.video-stream',
} as const;

export const UI_MACRODECK_COMPONENTS_WELL_KNOWN: readonly string[] = [
  UiMacroDeckComponents.DynamicText, UiMacroDeckComponents.ClockDial,
  UiMacroDeckComponents.ProgressBar, UiMacroDeckComponents.ProgressText, UiMacroDeckComponents.VideoStream,
];
