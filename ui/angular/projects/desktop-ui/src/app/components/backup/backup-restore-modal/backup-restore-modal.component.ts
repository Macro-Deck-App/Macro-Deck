import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BACKUP_COMPONENT_GROUP_IDS, BackupComponentCatalogEntry, BackupComponentGroup, BackupComponentGroupInfo, BackupDependencyWarningDto, backupComponentGroupLabel, backupComponentGroupLabels, backupDependencyWarningMessage } from '@macro-deck/runtime';
import { ButtonComponent, CheckboxComponent, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';
import { formatBytes } from '../../../util/format-bytes';

const ALL_GROUP_IDS: BackupComponentGroup[] = [...BACKUP_COMPONENT_GROUP_IDS];

@Component({
  selector: 'shared-backup-restore-modal',
  standalone: true,
  imports: [FormsModule, ModalComponent, ButtonComponent, CheckboxComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './backup-restore-modal.component.html',
  styleUrls: ['./backup-restore-modal.component.scss'],
})
export class BackupRestoreModalComponent {
  @Input() backupName = '';

  @Input({ required: true })
  set catalog(value: BackupComponentCatalogEntry[]) {
    this.catalogState.set(value ?? []);
  }

  @Input()
  set componentsInfo(value: BackupComponentGroupInfo[] | null | undefined) {
    this.componentsInfoState.set(value ?? []);
  }

  @Output() confirmed = new EventEmitter<BackupComponentGroup[]>();
  @Output() cancelled = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  private readonly localization = inject(LocalizationService);
  private readonly translate = (key: string, args?: Record<string, unknown>): string =>
    this.localization.translateKey(key, args);

  protected readonly labels = computed(() => backupComponentGroupLabels(this.translate));
  protected readonly formatBytes = formatBytes;

  protected readonly catalogState = signal<BackupComponentCatalogEntry[]>([]);
  protected readonly componentsInfoState = signal<BackupComponentGroupInfo[]>([]);
  protected readonly selected = signal<Set<BackupComponentGroup>>(new Set(ALL_GROUP_IDS));

  private readonly requiresMap = computed(() => {
    const map = new Map<BackupComponentGroup, BackupComponentGroup[]>();
    for (const entry of this.catalogState()) {
      map.set(entry.id, entry.requires);
    }
    return map;
  });

  private readonly infoMap = computed(() => {
    const map = new Map<BackupComponentGroup, BackupComponentGroupInfo>();
    for (const info of this.componentsInfoState()) {
      map.set(info.id, info);
    }
    return map;
  });

  protected readonly warnings = computed<BackupDependencyWarningDto[]>(() => {
    const selected = this.selected();
    const result: BackupDependencyWarningDto[] = [];
    for (const [group, requires] of this.requiresMap()) {
      if (!selected.has(group)) {
        continue;
      }
      for (const dependency of requires) {
        if (!selected.has(dependency)) {
          result.push({ group, missingDependency: dependency });
        }
      }
    }
    return result;
  });

  protected readonly canContinue = computed(() => this.selected().size > 0);

  protected isSelected(group: BackupComponentGroup): boolean {
    return this.selected().has(group);
  }

  protected sizeFor(group: BackupComponentGroup): string | null {
    const info = this.infoMap().get(group);
    return info ? formatBytes(info.byteSize) : null;
  }

  protected warningsFor(group: BackupComponentGroup): BackupDependencyWarningDto[] {
    return this.warnings().filter(warning => warning.group === group);
  }

  protected warningMessage(warning: BackupDependencyWarningDto): string {
    return backupDependencyWarningMessage(warning.group, warning.missingDependency, this.translate);
  }

  protected toggleGroup(group: BackupComponentGroup, checked: boolean): void {
    const next = new Set(this.selected());
    if (checked) {
      next.add(group);
      for (const dependency of this.transitiveRequires(group)) {
        next.add(dependency);
      }
    } else {
      next.delete(group);
    }
    this.selected.set(next);
  }

  protected selectAll(): void {
    this.selected.set(new Set(ALL_GROUP_IDS));
  }

  protected clearAll(): void {
    this.selected.set(new Set());
  }

  onConfirm(): void {
    if (!this.canContinue()) {
      return;
    }
    const groups = [...this.selected()];
    dismissModal(this.modal, () => this.confirmed.emit(groups));
  }

  onCancel(): void {
    dismissModal(this.modal, () => this.cancelled.emit());
  }

  private transitiveRequires(group: BackupComponentGroup): BackupComponentGroup[] {
    const map = this.requiresMap();
    const seen = new Set<BackupComponentGroup>();
    const stack = [...(map.get(group) ?? [])];
    while (stack.length > 0) {
      const dependency = stack.pop()!;
      if (seen.has(dependency)) {
        continue;
      }
      seen.add(dependency);
      stack.push(...(map.get(dependency) ?? []));
    }
    return [...seen];
  }
}
