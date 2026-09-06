import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';

import { WidgetIconRef, iconPackRef, iconPackReferenceOf } from '@macro-deck/runtime';
import { ButtonComponent, IconImageService, TranslatePipe } from '@shared';
import { IconPickerDialogComponent } from '../icon-picker-dialog/icon-picker-dialog.component';
import { IconModel } from '../../services/icon-pack.service';

@Component({
  selector: 'shared-widget-icon-control',
  standalone: true,
  imports: [ButtonComponent, IconPickerDialogComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="icon-row">
      @if (showPreview) {
        <span class="icon-preview" [class.empty]="!iconUrl">
          @if (iconUrl) {
            <img [src]="iconUrl" [alt]="'macrodeck.app:Widgets.Appearance.Icon.SelectedIconAlt' | translate" />
          } @else {
            <span class="icon icon-image icon-lg" aria-hidden="true"></span>
          }
        </span>
      }
      <shared-button variant="secondary" [disabled]="disabled" (click)="showPicker.set(true)">
        {{ (icon ? 'macrodeck.app:Widgets.Appearance.Icon.ChangeIcon' : 'macrodeck.app:Widgets.Appearance.Icon.ChooseIcon') | translate }}
      </shared-button>
      @if (icon) {
        <shared-button variant="ghost" [disabled]="disabled" [attr.aria-label]="'macrodeck.app:Widgets.Appearance.Icon.RemoveIcon' | translate" (click)="valueChange.emit(undefined)">
          {{ 'macrodeck:Common.Remove' | translate }}
        </shared-button>
      }
    </div>

    @if (showPicker()) {
      <shared-icon-picker-dialog (iconPicked)="onIconSelected($event)" (closed)="showPicker.set(false)" />
    }
  `,
  styles: `
    .icon-row { display: flex; align-items: center; gap: var(--space-2); flex-wrap: wrap; }
    .icon-preview {
      display: inline-flex; align-items: center; justify-content: center; width: 1.8125rem; height: 1.8125rem;
      flex: 0 0 auto; overflow: hidden; border: 1px solid var(--color-border); border-radius: var(--radius-sm);
      background: var(--color-bg-primary);
    }
    .icon-preview img { width: 100%; height: 100%; object-fit: cover; }
    .icon-preview.empty .icon { background-color: var(--color-text-muted); }
  `,
})
export class WidgetIconControlComponent {
  private readonly iconImage = inject(IconImageService);

  @Input() icon: WidgetIconRef | undefined;
  @Input() showPreview = true;
  @Input() disabled = false;
  @Output() valueChange = new EventEmitter<WidgetIconRef | undefined>();

  protected readonly showPicker = signal(false);

  protected get iconUrl(): string | null {
    return this.iconImage.getIconUrl(iconPackReferenceOf(this.icon), 128);
  }

  protected onIconSelected(icon: IconModel): void {
    this.showPicker.set(false);
    this.valueChange.emit(iconPackRef(icon.id));
  }
}
