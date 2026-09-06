import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonComponent, InputComponent, TranslatePipe } from '@shared';

interface KeyValueRow {
  key: string;
  value: string;
}

@Component({
  selector: 'shared-keyvalue-editor',
  standalone: true,
  imports: [FormsModule, InputComponent, ButtonComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="kv-root">
      @for (row of rows; track $index) {
        <div class="kv-row">
          <shared-input
            class="kv-key"
            [placeholder]="'macrodeck.app:Forms.KeyValueEditor.Key' | translate"
            [ngModel]="row.key"
            (ngModelChange)="updateRow($index, $event, row.value)" />
          <shared-input
            class="kv-value"
            [placeholder]="'macrodeck.app:Forms.KeyValueEditor.Value' | translate"
            [ngModel]="row.value"
            (ngModelChange)="updateRow($index, row.key, $event)" />
          <shared-button variant="danger-ghost" size="icon"
            [ariaLabel]="'macrodeck.app:Forms.KeyValueEditor.RemoveEntry' | translate"
            (click)="removeRow($index)">
            <span class="icon icon-x icon-sm" aria-hidden="true"></span>
          </shared-button>
        </div>
      }
      <shared-button variant="add" (click)="addRow()">
        <span class="icon icon-plus icon-sm" aria-hidden="true"></span>
        {{ 'macrodeck.app:Forms.KeyValueEditor.AddEntry' | translate }}
      </shared-button>
    </div>
  `,
  styles: [`
    .kv-root { display: flex; flex-direction: column; gap: var(--space-1); flex: 1 1 0; }
    .kv-row { display: flex; gap: var(--space-2); align-items: center; }
    .kv-key { flex: 1 1 35%; min-width: 6.5rem; }
    .kv-value { flex: 1 1 65%; min-width: 6.5rem; }
    .kv-row shared-button { flex: 0 0 auto; }
  `],
})
export class KeyValueEditorComponent {
  rows: KeyValueRow[] = [];

  @Input()
  set value(map: Record<string, string> | null) {
    const incoming = Object.entries(map ?? {});
    const current = this.rows.filter(r => r.key !== '');
    const differs = incoming.length !== current.length
      || incoming.some(([k, v], i) => current[i]?.key !== k || current[i]?.value !== v);
    if (differs) {
      this.rows = incoming.map(([key, value]) => ({ key, value }));
    }
  }

  @Output() valueChange = new EventEmitter<Record<string, string>>();

  addRow(): void {
    this.rows = [...this.rows, { key: '', value: '' }];
  }

  removeRow(index: number): void {
    this.rows = this.rows.filter((_, i) => i !== index);
    this.emit();
  }

  updateRow(index: number, key: string, value: string): void {
    this.rows = this.rows.map((row, i) => (i === index ? { key, value } : row));
    this.emit();
  }

  private emit(): void {
    const map: Record<string, string> = {};
    for (const row of this.rows) {
      if (row.key.trim() !== '') {
        map[row.key] = row.value;
      }
    }
    this.valueChange.emit(map);
  }
}
