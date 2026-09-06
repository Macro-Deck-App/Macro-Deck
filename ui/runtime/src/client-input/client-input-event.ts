export type ClientInputEvent =
  | { readonly kind: 'focusMove'; readonly delta: number }
  | { readonly kind: 'activate' }
  | { readonly kind: 'back' }
  | { readonly kind: 'selectIndex'; readonly index: number };
