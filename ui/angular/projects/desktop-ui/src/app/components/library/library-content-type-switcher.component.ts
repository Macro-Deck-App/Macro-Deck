import { ChangeDetectionStrategy, Component, Input, computed, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService, SegmentedControlComponent, SegmentedOption } from '@shared';
import { LIBRARY_CONTENT_TYPES, LIBRARY_ROUTE } from '../../domain/library-content-type';

export const LIBRARY_OVERVIEW_ID = 'overview';

@Component({
  selector: 'app-library-content-type-switcher',
  standalone: true,
  imports: [SegmentedControlComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-segmented-control
      [ariaLabel]="ariaLabel()"
      [options]="options()"
      [value]="activeId"
      (valueChange)="open($event)" />
  `,
})
export class LibraryContentTypeSwitcherComponent {
  @Input() activeId = LIBRARY_OVERVIEW_ID;

  private readonly router = inject(Router);
  private readonly localization = inject(LocalizationService);
  private readonly contentTypes = inject(LIBRARY_CONTENT_TYPES);

  protected readonly ariaLabel = computed(() =>
    this.localization.translateKey(AppStrings.Library.Page.ContentTypeAriaLabel));

  protected readonly options = computed<SegmentedOption[]>(() => [
    {
      value: LIBRARY_OVERVIEW_ID,
      label: this.localization.translateKey(AppStrings.Library.Page.OverviewOption),
    },
    ...this.contentTypes.map(type => ({
      value: type.id,
      label: this.localization.translateKey(type.labelKey),
    })),
  ]);

  protected open(id: string): void {
    const route = id === LIBRARY_OVERVIEW_ID
      ? LIBRARY_ROUTE
      : this.contentTypes.find(type => type.id === id)?.route;
    if (route) {
      void this.router.navigateByUrl(route);
    }
  }
}
