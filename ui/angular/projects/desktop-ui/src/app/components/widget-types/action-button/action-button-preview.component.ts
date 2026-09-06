import { Component, EventEmitter, Output, ChangeDetectionStrategy, computed, input } from '@angular/core';

import { ActionButtonData, ButtonStateDefinition, WIDGET_REFERENCE_CELL_SIZE, WidgetType } from '@macro-deck/runtime';
import { TranslatePipe, UiTreeWidgetComponent } from '@shared';
import { IconDropTargetDirective } from '../../icon-drop/icon-drop-target.directive';

const BOX_PX = 100;

const SINGLE_TILE_KEY = '';

export const EDITOR_PREVIEW_ICON_SIZE = 128;

@Component({
  selector: 'shared-action-button-preview',
  standalone: true,
  imports: [UiTreeWidgetComponent, IconDropTargetDirective, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="preview-container">
      <div class="preview-box">
        <div class="preview-frame">
          <div class="preview-button-wrap"
            [style.--widget-scale]="previewScale()"
            [style.width.px]="boxWidth()" [style.height.px]="boxHeight()"
            sharedIconDropTarget (iconPathDropped)="iconDropped.emit({ stateId: tileKey(), path: $event })">
            <shared-ui-tree-widget
              class="preview-widget"
              [widgetType]="widgetType"
              [data]="previewData()"
              [width]="boxWidth()"
              [height]="boxHeight()"
              [variableScopeWidgetId]="variableScopeWidgetId()"
              [disabled]="true">
            </shared-ui-tree-widget>
            @if (importingStateId() === tileKey()) {
              <span class="preview-importing" [attr.aria-label]="'macrodeck.app:Widgets.ActionButtonPreview.ImportingIcon' | translate"><span class="preview-spinner"></span></span>
            }
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .preview-container { display: flex; flex-wrap: wrap; gap: 0.625rem; justify-content: center; padding: 0.375rem; }
    .preview-box {
      display: flex; flex-direction: column; align-items: center; gap: 0.3125rem; min-width: 110px;
      padding: 0.3125rem; border-radius: 0.375rem;
    }
    .preview-frame { width: 100px; height: 100px; display: flex; align-items: center; justify-content: center; }
    .preview-button-wrap {
      position: relative; border-radius: calc(22px * var(--widget-scale, 1)); overflow: hidden;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.2); box-sizing: border-box;
    }
    .preview-widget { display: block; width: 100%; height: 100%; }
    .preview-button-wrap.icon-drop-active::after {
      content: ''; position: absolute; inset: 0; z-index: 4; border-radius: inherit;
      border: 2px dashed var(--color-accent, #6366f1);
      background-color: color-mix(in srgb, var(--color-accent, #6366f1) 30%, transparent);
      box-shadow: inset 0 0 0 1px rgba(0, 0, 0, 0.35);
    }
    .preview-importing {
      position: absolute; inset: 0; z-index: 5; display: flex; align-items: center; justify-content: center;
      border-radius: inherit; background-color: rgba(0, 0, 0, 0.45);
    }
    .preview-spinner {
      width: 24px; height: 24px;
      border: 3px solid color-mix(in srgb, var(--color-accent) 30%, transparent);
      border-top-color: var(--color-accent); border-radius: 50%;
      animation: ds-spin 0.8s linear infinite;
    }
  `]
})
export class ActionButtonPreviewComponent {
  protected readonly widgetType = WidgetType.ActionButton;
  protected readonly tileKey = computed(() => this.editedStateId() ?? SINGLE_TILE_KEY);

  readonly data = input<ActionButtonData>({});
  readonly selectedStateId = input<string | undefined>(undefined);
  readonly importingStateId = input<string | null>(null);
  readonly aspectRatio = input<number>(1);
  readonly variableScopeWidgetId = input<string | undefined>(undefined);
  @Output() iconDropped = new EventEmitter<{ stateId: string; path: string }>();

  private readonly hasStates = computed(() => !!this.data().stateMode && (this.data().states?.length ?? 0) > 0);

  private readonly editedStateId = computed<string | undefined>(() => {
    if (!this.hasStates()) return undefined;
    const states: ButtonStateDefinition[] = this.data().states ?? [];
    return (states.find(state => state.id === this.selectedStateId()) ?? states[0])?.id;
  });

  protected readonly previewData = computed<ActionButtonData>(() => ({
    ...this.data(),
    activeStateId: this.editedStateId(),
  }));

  protected readonly boxWidth = computed(() => {
    const ratio = this.safeAspectRatio();
    return Math.round(ratio >= 1 ? BOX_PX : BOX_PX * ratio);
  });
  protected readonly boxHeight = computed(() => {
    const ratio = this.safeAspectRatio();
    return Math.round(ratio >= 1 ? BOX_PX / ratio : BOX_PX);
  });

  protected readonly previewScale = computed(() =>
    Math.min(this.boxWidth(), this.boxHeight()) / WIDGET_REFERENCE_CELL_SIZE);

  private safeAspectRatio(): number {
    const ratio = this.aspectRatio();
    return Number.isFinite(ratio) && ratio > 0 ? ratio : 1;
  }
}
