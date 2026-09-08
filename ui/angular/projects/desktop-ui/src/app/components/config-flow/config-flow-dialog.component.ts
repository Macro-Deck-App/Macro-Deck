import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';

import { AppStrings, ConfigFlowStepDto, Strings, UiConfigEvents, UiConfigProperties, emitsEvent, nodeString, nodeText, resolveLocalizedText } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, LocalizedTextPipe, ModalComponent, ToggleSwitchComponent, TranslatePipe, dismissModal } from '@shared';
import type { UiNode, UiNodeEvent } from '@macro-deck/runtime';
import { ConfigFlowService } from '../../services/config-flow.service';
import { ExternalLinkService } from '../../services/external-link.service';
import { CopyValueComponent } from '../copy-value/copy-value.component';
import { UiTreeComponent } from '../ui-render/ui-tree.component';
import { ConfigFieldComponent } from './config-field.component';
import { ConfigFlowInstructionsComponent } from './config-flow-instructions.component';

const COPY_FALLBACK_Z_INDEX = 1100;

@Component({
  selector: 'shared-config-flow-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ModalComponent,
    ButtonComponent,
    ConfigFieldComponent,
    ToggleSwitchComponent,
    CopyValueComponent,
    ConfigFlowInstructionsComponent,
    UiTreeComponent,
    LocalizedTextPipe,
    TranslatePipe,
  ],
  template: `
    <shared-modal [heading]="title" [showFooter]="true" maxWidth="520px" (close)="onClose()">
      @if (flow.starting()) {
        <p class="cfd-status">{{ 'macrodeck:Common.Loading' | translate }}</p>
      } @else if (flow.notSupported()) {
        <p class="cfd-status">{{ 'macrodeck.app:ConfigFlow.NotSupported' | translate }}</p>
      } @else if (flow.done()) {
        <div class="cfd-done">
          <span class="cfd-check" aria-hidden="true">
            <span class="icon icon-check icon-md"></span>
          </span>
          <p>{{ 'macrodeck.app:ConfigFlow.SetupComplete' | translate }}</p>
        </div>
      } @else if (flow.waitingForAuth()) {
        <div class="cfd-waiting">
          <span class="cfd-spinner" aria-hidden="true"></span>
          <p>{{ 'macrodeck.app:ConfigFlow.WaitingForAuthorization' | translate }}</p>
          <p class="cfd-status">{{ 'macrodeck.app:ConfigFlow.CompleteLoginInBrowser' | translate }}</p>
          @if (flow.message()) {
            <div class="cfd-banner">{{ flow.message() }}</div>
          }
        </div>
      } @else if (root; as node) {
        <shared-ui-tree [root]="node" (nodeEvent)="onTreeEvent($event)" />
      } @else if (flow.step(); as step) {
        @if (step.description) {
          <p class="cfd-description">{{ step.description | localizedText }}</p>
        }
        @if (step.values?.length) {
          <div class="cfd-values">
            @for (value of step.values; track $index) {
              <shared-copy-value
                [label]="value.label | localizedText"
                [value]="value.value"
                [fallbackZIndex]="copyFallbackZIndex" />
            }
          </div>
        }
        @if (step.instructions?.length) {
          <shared-config-flow-instructions
            [instructions]="step.instructions"
            [fallbackZIndex]="copyFallbackZIndex" />
        }
        @if (step.links?.length) {
          <div class="cfd-links">
            @for (link of step.links; track link.url) {
              <a
                class="cfd-doclink"
                [href]="link.url"
                target="_blank"
                rel="noopener noreferrer"
                (click)="onLinkClick($event, link.url)">
                <span>{{ link.label | localizedText }}</span>
                <span class="icon icon-external-link icon-xs" aria-hidden="true"></span>
              </a>
            }
          </div>
        }
        @if (flow.message()) {
          <div class="cfd-banner">{{ flow.message() }}</div>
        }
        @if (hasSetupContent(step) && step.fields.length) {
          <div class="cfd-config-separator"></div>
          <div class="cfd-config-label">{{ 'macrodeck.app:ConfigFlow.Configuration' | translate }}</div>
        }
        <div class="cfd-fields">
          @for (field of step.fields; track field.name) {
            <shared-config-field
              [field]="field"
              [value]="flow.values()[field.name]"
              [error]="flow.fieldErrors()[field.name] || null"
              [secretStored]="flow.storedSecretFields().has(field.name)"
              (clearSecret)="flow.clearStoredSecret(field.name)"
              (valueChange)="flow.setValue(field.name, $event)">
            </shared-config-field>
          }
        </div>
        @if (step.advancedFields?.length) {
          <div class="cfd-advanced">
            <shared-toggle-switch
              [label]="advancedConfigurationLabel"
              [checked]="showAdvanced()"
              (changed)="onToggleAdvanced($event)">
            </shared-toggle-switch>
            @if (showAdvanced()) {
              <div class="cfd-fields">
                @for (field of step.advancedFields; track field.name) {
                  <shared-config-field
                    [field]="field"
                    [value]="flow.values()[field.name]"
                    [error]="flow.fieldErrors()[field.name] || null"
                    [secretStored]="flow.storedSecretFields().has(field.name)"
                    (clearSecret)="flow.clearStoredSecret(field.name)"
                    (valueChange)="flow.setValue(field.name, $event)">
                  </shared-config-field>
                }
              </div>
            }
          </div>
        }
      }

      <div modal-footer>
        @if (flow.done()) {
          <shared-button variant="primary" (click)="onClose()">{{ 'macrodeck:Common.Done' | translate }}</shared-button>
        } @else if (flow.waitingForAuth()) {
          <shared-button variant="secondary" (click)="onClose()">{{ 'macrodeck:Common.Cancel' | translate }}</shared-button>
        } @else {
          <shared-button variant="secondary" (click)="onClose()">{{ 'macrodeck:Common.Cancel' | translate }}</shared-button>
          <shared-button
            variant="primary"
            [disabled]="!flow.canSubmit() || flow.submitting()"
            [loading]="flow.submitting()"
            (click)="onSubmit()">
            {{ submitLabel }}
          </shared-button>
        }
      </div>
    </shared-modal>
  `,
  styleUrls: ['./config-flow-dialog.component.scss'],
})
export class ConfigFlowDialogComponent implements OnChanges {
  protected readonly flow = inject(ConfigFlowService);
  private readonly externalLinks = inject(ExternalLinkService);
  private readonly localization = inject(LocalizationService);

  protected readonly copyFallbackZIndex = COPY_FALLBACK_Z_INDEX;

  @Input() integrationId: string | null = null;
  @Input() entryId: string | null = null;
  @Input() configurationTitle: string | null = null;

  @Input() root: UiNode | null = null;

  @Output() closed = new EventEmitter<boolean>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  private readonly advancedOpened = signal(false);

  protected readonly showAdvanced = computed(() => {
    if (this.advancedOpened()) return true;
    const values = this.flow.values();
    return (this.flow.step()?.advancedFields ?? []).some(field => {
      const value = values[field.name];
      return value !== undefined && value !== null && value !== '' && value !== false;
    });
  });

  async ngOnChanges(changes: SimpleChanges): Promise<void> {
    if (changes['integrationId'] && this.integrationId) {
      this.advancedOpened.set(false);
      await this.flow.start(this.integrationId, {
        entryId: this.entryId ?? undefined,
        title: this.configurationTitle ?? undefined,
      });
    }
  }

  onToggleAdvanced(open: boolean): void {
    this.advancedOpened.set(open);

    if (!open) {
      for (const field of this.flow.step()?.advancedFields ?? []) {
        this.flow.setValue(field.name, '');
      }
    }
  }

  get title(): string {
    if (this.root) {
      const title =
        nodeText(this.root, UiConfigProperties.Title, this.localization)
        ?? nodeText(this.root, UiConfigProperties.Label, this.localization);
      return title ?? this.localization.translateKey(Strings.ConfigFlow.SetUpIntegration);
    }

    const step = this.flow.step();
    const stepTitle = step ? resolveLocalizedText(step.title, this.localization) : '';
    return stepTitle || this.localization.translateKey(Strings.ConfigFlow.SetUpIntegration);
  }

  get submitLabel(): string {
    return this.localization.translateKey(Strings.Common.Continue);
  }

  get advancedConfigurationLabel(): string {
    return this.localization.translateKey(AppStrings.ConfigFlow.AdvancedConfiguration);
  }

  protected hasSetupContent(step: ConfigFlowStepDto): boolean {
    return !!step.description || !!step.values?.length || !!step.instructions?.length;
  }

  async onSubmit(): Promise<void> {
    if (this.root && !emitsEvent(this.root, UiConfigEvents.Submit)) return;
    await this.flow.submit();
  }

  protected onTreeEvent(event: UiNodeEvent): void {
    this.flow.sendTreeEvent(event);

    if (event.name === UiConfigEvents.Change) {
      this.flow.setValue(event.nodeId, event.data);
      return;
    }

    if (event.name === UiConfigEvents.Activate) {
      const url = this.root && nodeString(findNode(this.root, event.nodeId), UiConfigProperties.Url);
      if (url) this.externalLinks.open(url);
      return;
    }

    if (!this.root || event.nodeId !== this.root.id) return;

    switch (event.name) {
      case UiConfigEvents.Submit:
        void this.onSubmit();
        break;
      case UiConfigEvents.Cancel:
        this.onClose();
        break;
    }
  }

  onLinkClick(event: MouseEvent, url: string): void {
    event.preventDefault();
    this.externalLinks.open(url);
  }

  onClose(): void {
    dismissModal(this.modal, () => {
      const completed = this.flow.done();
      this.flow.reset();
      this.closed.emit(completed);
    });
  }
}

function findNode(node: UiNode, id: string): UiNode | undefined {
  if (node.id === id) return node;
  for (const child of node.children ?? []) {
    const found = findNode(child, id);
    if (found) return found;
  }
  return node.fallback ? findNode(node.fallback, id) : undefined;
}
