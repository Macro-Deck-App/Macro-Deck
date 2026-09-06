import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnDestroy,
  Output,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AppStrings, KEYBOARD_MODIFIERS, KeyboardComboValue, formatCombo, keyFromEvent, modifierLabel, supportedKeyGroups } from '@macro-deck/runtime';
import { LocalizationService, SegmentedControlComponent, SegmentedOption, TranslatePipe } from '@shared';
import { HotkeyCaptureService } from '../../../services/hotkey-capture.service';
import { SelectComponent, SelectOption } from '../select/select.component';

type KeyMode = 'record' | 'select';

@Component({
  selector: 'shared-keyboard-combo-editor',
  standalone: true,
  imports: [FormsModule, SelectComponent, SegmentedControlComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="kce-root">
      <div class="kce-modifiers">
        @for (modifier of modifiers; track modifier) {
          <button
            type="button"
            class="kce-mod"
            [class.kce-mod-active]="hasModifier(modifier)"
            (click)="toggleModifier(modifier)">
            {{ label(modifier) }}
          </button>
        }
      </div>

      <div class="kce-key-row">
        <shared-segmented-control
          class="kce-mode"
          [ariaLabel]="'macrodeck.app:Forms.KeyboardComboEditor.KeyEntryMode' | translate"
          [options]="modeOptions()"
          [value]="mode()"
          (valueChange)="setMode($any($event))" />

        @if (mode() === 'record') {
          <button
            type="button"
            class="kce-record"
            [class.kce-recording]="recording()"
            (click)="startRecording($event)">
            @if (recording()) {
              <span class="kce-hint">{{ 'macrodeck.app:Forms.KeyboardComboEditor.RecordingHint' | translate }}</span>
            } @else if (key) {
              <span class="kce-key-name">{{ key }}</span>
            } @else {
              <span class="kce-hint">{{ 'macrodeck.app:Forms.HotkeyRecorder.ClickToRecord' | translate }}</span>
            }
          </button>
        } @else {
          <shared-select
            class="kce-select"
            [placeholder]="'macrodeck.app:Forms.KeyboardComboEditor.SelectKeyPlaceholder' | translate"
            [options]="keyOptions()"
            [ngModel]="key"
            (ngModelChange)="onSelectKey($event)" />
        }
      </div>

      <div class="kce-preview" [class.kce-preview-empty]="!hasValue">
        {{ hasValue ? preview : ('macrodeck.app:Forms.KeyboardComboEditor.NoKeySet' | translate) }}
      </div>
    </div>
  `,
  styleUrls: ['./keyboard-combo-editor.component.scss'],
})
export class KeyboardComboEditorComponent implements OnDestroy {
  @Input() value: KeyboardComboValue | null = null;
  @Output() valueChange = new EventEmitter<KeyboardComboValue>();

  readonly modifiers = KEYBOARD_MODIFIERS;

  readonly keyGroups = computed(() => supportedKeyGroups(key => this.localization.translateKey(key)));
  readonly keyOptions = computed<SelectOption[]>(() => this.keyGroups().flatMap(group =>
    group.keys.map(option => ({ value: option.value, label: option.label, group: group.label }))));
  readonly mode = signal<KeyMode>('record');
  readonly recording = signal(false);

  private readonly capture = inject(HotkeyCaptureService);
  private readonly localization = inject(LocalizationService);

  readonly modeOptions = computed<SegmentedOption[]>(() => [
    { value: 'record', label: this.localization.translateKey(AppStrings.Forms.KeyboardComboEditor.RecordMode) },
    { value: 'select', label: this.localization.translateKey(AppStrings.Forms.KeyboardComboEditor.SelectMode) },
  ]);

  get key(): string {
    return this.value?.key ?? '';
  }

  get currentModifiers(): string[] {
    return this.value?.modifiers ?? [];
  }

  get hasValue(): boolean {
    return !!this.key || this.currentModifiers.length > 0;
  }

  get preview(): string {
    return formatCombo(this.currentModifiers, this.key);
  }

  label(modifier: string): string {
    return modifierLabel(modifier);
  }

  ngOnDestroy(): void {
    this.stopRecording();
  }

  setMode(mode: KeyMode): void {
    this.mode.set(mode);
    this.stopRecording();
  }

  hasModifier(modifier: string): boolean {
    return this.currentModifiers.includes(modifier);
  }

  toggleModifier(modifier: string): void {
    const next = this.hasModifier(modifier)
      ? this.currentModifiers.filter(m => m !== modifier)
      : [...this.currentModifiers, modifier];
    this.emit(next, this.key);
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

  onSelectKey(key: string): void {
    this.emit(this.currentModifiers, key);
  }

  private record(event: KeyboardEvent): void {
    const modifiers: string[] = [];
    if (event.ctrlKey) modifiers.push('Ctrl');
    if (event.shiftKey) modifiers.push('Shift');
    if (event.altKey) modifiers.push('Alt');
    if (event.metaKey) modifiers.push('Meta');

    this.emit(modifiers.length > 0 ? modifiers : this.currentModifiers, keyFromEvent(event));
    this.stopRecording();
  }

  private emit(modifiers: string[], key: string): void {
    this.value = { modifiers, key };
    this.valueChange.emit(this.value);
  }
}
