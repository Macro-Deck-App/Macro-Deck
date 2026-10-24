// Mirrored from ui-model/src/MacroDeck.Ui/Config/UiPageLayoutValues.cs and UiPageLayoutElements.cs, same
// declaration order. Open vocabularies: a value a reader does not know is drawn as the documented default.
export const UiButtonVariants = {
  Primary: 'primary',
  Secondary: 'secondary',
  Danger: 'danger',
  Ghost: 'ghost',
} as const;

export const UiStackAlignments = {
  Start: 'start',
  Center: 'center',
  End: 'end',
  SpaceBetween: 'space-between',
} as const;

export const UiStackGaps = {
  None: 'none',
  Small: 'small',
  Medium: 'medium',
  Large: 'large',
} as const;

export const UiStackVariants = {
  Card: 'card',
} as const;

// A split node always has exactly three stack children, in this order; the keys end their ids.
export const UiSplitRegions = {
  RailActions: 'rail-actions',
  Rail: 'rail',
  Detail: 'detail',
} as const;
