import { InjectionToken } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';

export interface LibraryContentType {
  readonly id: string;
  readonly route: string;
  readonly labelKey: string;
  readonly descriptionKey: string;
  readonly icon: string;
}

export const LIBRARY_ROUTE = '/library';

export const ICON_PACKS_CONTENT_TYPE = 'icon-packs';

export const FONTS_CONTENT_TYPE = 'fonts';

export const LIBRARY_CONTENT_TYPES = new InjectionToken<readonly LibraryContentType[]>(
  'library.contentTypes',
  {
    providedIn: 'root',
    factory: () => [
      {
        id: ICON_PACKS_CONTENT_TYPE,
        route: '/library/icon-packs',
        labelKey: AppStrings.Nav.IconPacks,
        descriptionKey: AppStrings.Library.Page.IconPacksDescription,
        icon: 'image',
      },
      {
        id: FONTS_CONTENT_TYPE,
        route: '/library/fonts',
        labelKey: AppStrings.Nav.Fonts,
        descriptionKey: AppStrings.Library.Page.FontsDescription,
        icon: 'type',
      },
    ],
  },
);
