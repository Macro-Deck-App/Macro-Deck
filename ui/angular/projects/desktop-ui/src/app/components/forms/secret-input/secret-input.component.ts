import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings, SecretKind, SecretReference } from '@macro-deck/runtime';
import { ApiService, ButtonComponent, ButtonGroupComponent, LocalizationService, TranslatePipe } from '@shared';

@Component({
  selector: 'shared-secret-input',
  standalone: true,
  imports: [FormsModule, ButtonComponent, ButtonGroupComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-button-group class="si-root">
      @if (editing() || !value) {
        <input
          class="si-input"
          [type]="revealed() ? 'text' : 'password'"
          [placeholder]="value ? ('macrodeck.app:Forms.SecretInput.EnterNewValue' | translate) : placeholder"
          autocomplete="new-password"
          [ngModel]="draft()"
          (ngModelChange)="draft.set($event)"
          (keydown.enter)="save()">
        <shared-button variant="secondary" [disabled]="draft().trim() === ''" (click)="save()">
          {{ 'macrodeck:Common.Save' | translate }}
        </shared-button>
        @if (value) {
          <shared-button variant="ghost" (click)="cancelEdit()">{{ 'macrodeck:Common.Cancel' | translate }}</shared-button>
        }
      } @else {
        <span class="si-stored" [title]="revealedValue() ?? ('macrodeck.app:Forms.SecretInput.StoredEncrypted' | translate)">
          {{ revealedValue() ?? '••••••••' }}
        </span>
        @if (kind === 'Password') {
          <shared-button
            variant="ghost"
            iconOnly
            [attr.aria-label]="revealedValue()
              ? ('macrodeck.app:Forms.SecretInput.HideValue' | translate)
              : ('macrodeck.app:Forms.SecretInput.RevealValue' | translate)"
            (click)="toggleReveal()">
            @if (revealedValue()) {
              <svg class="si-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
                stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                <path d="M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49"/>
                <path d="M14.084 14.158a3 3 0 0 1-4.242-4.242"/>
                <path d="M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143"/>
                <path d="m2 2 20 20"/>
              </svg>
            } @else {
              <svg class="si-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
                stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                <path d="M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0"/>
                <circle cx="12" cy="12" r="3"/>
              </svg>
            }
          </shared-button>
        }
        <shared-button variant="secondary" (click)="startEdit()">
          {{ 'macrodeck.app:Forms.SecretInput.Replace' | translate }}
        </shared-button>
        <shared-button
          variant="danger-ghost"
          iconOnly
          [attr.aria-label]="'macrodeck.app:Forms.SecretInput.RemoveSecret' | translate"
          (click)="remove()">
          <svg class="si-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
            stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            <path d="M18 6 6 18"/>
            <path d="m6 6 12 12"/>
          </svg>
        </shared-button>
      }
    </shared-button-group>
  `,
  styleUrls: ['./secret-input.component.scss'],
})
export class SecretInputComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  private placeholderOverride?: string;

  @Input() value: SecretReference | null = null;
  @Input() kind: SecretKind = 'Secret';

  @Input() set placeholder(value: string) {
    this.placeholderOverride = value;
  }
  get placeholder(): string {
    return this.placeholderOverride ?? this.localization.translateKey(AppStrings.Forms.SecretInput.EnterSecret);
  }

  @Output() valueChange = new EventEmitter<SecretReference | null>();

  readonly editing = signal(false);
  readonly draft = signal('');
  readonly revealed = signal(false);
  readonly revealedValue = signal<string | null>(null);

  startEdit(): void {
    this.draft.set('');
    this.editing.set(true);
  }

  cancelEdit(): void {
    this.editing.set(false);
    this.draft.set('');
  }

  async save(): Promise<void> {
    const plaintext = this.draft().trim();
    if (plaintext === '') return;

    try {
      if (this.value) {
        await this.api.updateSecret({ id: this.value.$secret, value: plaintext });
        this.valueChange.emit(this.value);
      } else {
        const response = await this.api.createSecret({ value: plaintext, kind: this.kind });
        this.value = { $secret: response.id };
        this.valueChange.emit(this.value);
      }
      this.editing.set(false);
      this.draft.set('');
      this.revealedValue.set(null);
    } catch {
    }
  }

  async toggleReveal(): Promise<void> {
    if (this.revealedValue() !== null) {
      this.revealedValue.set(null);
      return;
    }

    if (!this.value) return;

    const response = await this.api.revealSecret(this.value.$secret);
    if (response.value !== undefined && response.value !== null) {
      this.revealedValue.set(response.value);
    }
  }

  async remove(): Promise<void> {
    if (this.value) {
      try {
        await this.api.deleteSecret(this.value.$secret);
      } catch {
      }
    }
    this.value = null;
    this.revealedValue.set(null);
    this.valueChange.emit(null);
  }
}
