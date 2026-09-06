import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { IpcIntegrationActionCapability } from '@macro-deck/runtime';
import { ButtonComponent, LocalizedTextPipe, TranslatePipe } from '@shared';

@Component({
  selector: 'app-action-capability-row',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ButtonComponent, LocalizedTextPipe, TranslatePipe],
  host: { style: 'display: contents;' },
  templateUrl: './action-capability-row.component.html',
  styleUrls: ['./action-capability-row.component.scss'],
})
export class ActionCapabilityRowComponent {
  @Input({ required: true }) action!: IpcIntegrationActionCapability;

  @Output() open = new EventEmitter<void>();

  get isReady(): boolean {
    return this.action.availability === 'Ready';
  }
}
