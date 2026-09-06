import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { InputComponent, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../feedback/loading-state/loading-state.component';
import { ActionDefinitionModel, ActionService } from '../../../services/action.service';
import { ActionTesterComponent } from './action-tester.component';

@Component({
  selector: 'app-action-runner',
  standalone: true,
  imports: [FormsModule, EmptyStateComponent, InputComponent, LoadingStateComponent, ActionTesterComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './action-runner.component.html',
  styleUrls: ['./action-runner.component.scss'],
})
export class ActionRunnerComponent {
  protected readonly actionService = inject(ActionService);

  readonly integrationId = input<string | null>(null);
  readonly actionId = input<string | null>(null);
  readonly heading = input('');

  protected readonly search = signal('');
  private readonly selectedKey = signal<string | null>(null);
  private readonly appliedActionId = signal<string | null>(null);

  constructor() {
    // Actions load asynchronously, and the deep-linked action may arrive on a later tick than this
    // input - re-run whenever either the target id or the loaded list changes, not just once.
    // Deliberately reads the unfiltered list and applies each id once: depending on the *filtered*
    // list would re-select the deep-linked action every time the user typed in the search box,
    // undoing whatever they had picked since.
    effect(() => {
      const id = this.actionId();
      if (!id || this.appliedActionId() === id) {
        return;
      }

      const integrationId = this.integrationId();
      const action = this.actionService.actions()
        .find(a => a.id === id && (integrationId === null || a.integrationId === integrationId));
      if (action) {
        this.selectedKey.set(this.keyOf(action));
        this.appliedActionId.set(id);
      }
    });
  }

  protected readonly filteredActions = computed<ActionDefinitionModel[]>(() => {
    const integrationId = this.integrationId();
    const query = this.search().trim().toLowerCase();
    return this.actionService.actions()
      .filter(a => integrationId === null || a.integrationId === integrationId)
      .filter(a => query === ''
        || a.name.toLowerCase().includes(query)
        || a.id.toLowerCase().includes(query))
      .sort((a, b) => a.name.localeCompare(b.name));
  });

  protected readonly selectedAction = computed<ActionDefinitionModel | null>(() => {
    const key = this.selectedKey();
    if (!key) {
      return null;
    }
    return this.filteredActions().find(a => this.keyOf(a) === key) ?? null;
  });

  protected keyOf(action: ActionDefinitionModel): string {
    return `${action.integrationId}::${action.id}`;
  }

  protected isSelected(action: ActionDefinitionModel): boolean {
    return this.selectedKey() === this.keyOf(action);
  }

  protected select(action: ActionDefinitionModel): void {
    this.selectedKey.set(this.keyOf(action));
  }
}
