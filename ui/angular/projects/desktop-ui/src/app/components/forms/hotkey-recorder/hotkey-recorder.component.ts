import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnDestroy,
  Output,
  inject,
  signal,
} from '@angular/core';

import { HotkeyValue, keyFromEvent } from '@macro-deck/runtime';
import { ButtonComponent, ButtonGroupComponent, TranslatePipe } from '@shared';
import { HotkeyCaptureService } from '../../../services/hotkey-capture.service';

let nextHotkeyRecorderId = 0;

// The key press is picked up by HotkeyCaptureService rather than by a listener on the button, which
// would need the button to hold focus - something WebKit does not grant on click.
@Component({
  selector: 'shared-hotkey-recorder',
  standalone: true,
  imports: [ButtonComponent, ButtonGroupComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-button-group class="hk-root">
      <button
        type="button"
        class="hk-field"
        [class.hk-recording]="recording()"
        [attr.aria-labelledby]="ariaLabel ? nameId + ' ' + valueId : null"
        (click)="startRecording($event)">
        @if (recording()) {
          <span class="hk-hint" [id]="valueId">{{ 'macrodeck.app:Forms.HotkeyRecorder.RecordingHint' | translate }}</span>
        } @else if (value) {
          <span class="hk-value" [id]="valueId">{{ display() }}</span>
        } @else {
          <span class="hk-hint" [id]="valueId">{{ 'macrodeck.app:Forms.HotkeyRecorder.ClickToRecord' | translate }}</span>
        }
      </button>
      @if (ariaLabel) {
        <span [id]="nameId" hidden>{{ ariaLabel }}</span>
      }
      @if (value && !recording()) {
        <shared-button
          variant="danger-ghost"
          iconOnly
          [attr.aria-label]="'macrodeck.app:Forms.HotkeyRecorder.ClearHotkey' | translate"
          (click)="clear()">
          <svg class="hk-clear-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
            stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            <path d="M18 6 6 18"/>
            <path d="m6 6 12 12"/>
          </svg>
        </shared-button>
      }
    </shared-button-group>
  `,
  styleUrls: ['./hotkey-recorder.component.scss'],
})
export class HotkeyRecorderComponent implements OnDestroy {
  private readonly idPrefix = `hk-${++nextHotkeyRecorderId}`;
  protected readonly nameId = `${this.idPrefix}-name`;
  protected readonly valueId = `${this.idPrefix}-value`;

  @Input() value: HotkeyValue | null = null;
  @Input() ariaLabel: string | null = null;
  @Output() valueChange = new EventEmitter<HotkeyValue | null>();

  readonly recording = signal(false);

  private readonly capture = inject(HotkeyCaptureService);

  display(): string {
    if (!this.value) return '';
    return [...this.value.modifiers, this.value.key].join('+');
  }

  ngOnDestroy(): void {
    this.stopRecording();
  }

  startRecording(click: Event): void {
    if (this.recording()) {
      return;
    }

    this.recording.set(true);
    this.capture.start(click.currentTarget as HTMLElement, {
      key: event => this.record(event),
      cancel: () => this.recording.set(false),
    });
  }

  stopRecording(): void {
    if (!this.recording()) {
      return;
    }

    this.recording.set(false);
    this.capture.stop();
  }

  clear(): void {
    this.value = null;
    this.valueChange.emit(null);
  }

  private record(event: KeyboardEvent): void {
    const modifiers: string[] = [];
    if (event.ctrlKey) modifiers.push('Ctrl');
    if (event.altKey) modifiers.push('Alt');
    if (event.shiftKey) modifiers.push('Shift');
    if (event.metaKey) modifiers.push('Meta');

    const next: HotkeyValue = {
      modifiers,
      key: keyFromEvent(event),
      code: event.code,
    };

    this.value = next;
    this.valueChange.emit(next);
    this.stopRecording();
  }
}
