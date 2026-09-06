import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslatePipe, VariableService } from '@shared';
import { TemplateEditorComponent } from '../../../template-builder/template-editor.component';

const DRAFT_KEY = 'md.developerTools.templateDraft';

@Component({
  selector: 'app-template-tab',
  standalone: true,
  imports: [TranslatePipe, TemplateEditorComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './template-tab.component.html',
  styleUrls: ['./template-tab.component.scss'],
})
export class TemplateTabComponent {
  private readonly variableService = inject(VariableService);

  protected readonly template = signal(readDraft());
  protected readonly variables = computed(() => this.variableService.visibleForContext('global'));

  protected setTemplate(value: string): void {
    this.template.set(value);
    writeDraft(value);
  }
}

function readDraft(): string {
  try {
    return localStorage.getItem(DRAFT_KEY) ?? '';
  } catch {
    return '';
  }
}

function writeDraft(value: string): void {
  try {
    localStorage.setItem(DRAFT_KEY, value);
  } catch {
  }
}
