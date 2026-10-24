import { ChangeDetectionStrategy, Component, computed, forwardRef, inject, input, signal } from '@angular/core';

import { AppStrings, UiButtonVariants, UiConfigEvents, UiConfigPrimitives, UiConfigProperties, UiNode, UiSplitRegions, UiStackAlignments, UiStackGaps, UiStackVariants, UiTextSpanRun, emitsEvent, nodeBoolean, nodeNumber, nodeRaw, nodeString, nodeText, nodeTextSpans, textSpanStyle } from '@macro-deck/runtime';
import { FormsModule } from '@angular/forms';
import { ButtonComponent, type ButtonVariant, InputComponent, LocalizationService, ModalComponent, SegmentedControlComponent, SegmentedOption, ToggleSwitchComponent } from '@shared';
import { EmptyStateComponent } from '../feedback/empty-state/empty-state.component';
import { RailPageComponent } from '../rail-page/rail-page.component';
import { ConfirmationModalComponent } from '../overlay/confirmation-modal/confirmation-modal.component';
import { DropdownMenuComponent } from '../overlay/dropdown-menu/dropdown-menu.component';
import { CopyValueComponent } from '../copy-value/copy-value.component';
import { UiRenderContext } from './ui-render-context';
import { UI_CHROME_TYPES } from './ui-chrome-types.util';
import { UiNodeComponent } from './ui-node.component';
import { ExternalLinkService } from '../../services/external-link.service';

const Primitives = UiConfigPrimitives;
const Properties = UiConfigProperties;
const HexColor = /^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$/;
const HeadingLevels: Record<string, number> = { 'heading-1': 1, 'heading-2': 2, 'heading-3': 3 };
const ButtonVariants: readonly string[] = Object.values(UiButtonVariants);
const StackAlignments: readonly string[] = Object.values(UiStackAlignments);
const StackGaps: readonly string[] = Object.values(UiStackGaps);
const ListItemSeverities: readonly string[] = ['info', 'success', 'warning', 'error'];
const SplitRegionOrder = [UiSplitRegions.RailActions, UiSplitRegions.Rail, UiSplitRegions.Detail];

function isExternalUrl(url: string): boolean {
  try {
    const parsed = new URL(url, window.location.href);
    return (parsed.protocol === 'http:' || parsed.protocol === 'https:') && parsed.origin !== window.location.origin;
  } catch {
    return false;
  }
}

@Component({
  selector: 'shared-ui-chrome',
  standalone: true,
  imports: [
    forwardRef(() => UiNodeComponent),
    ButtonComponent,
    ConfirmationModalComponent,
    CopyValueComponent,
    DropdownMenuComponent,
    EmptyStateComponent,
    FormsModule,
    InputComponent,
    ModalComponent,
    RailPageComponent,
    SegmentedControlComponent,
    ToggleSwitchComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @switch (node().type) {
      @case (types.Flow) {
        <div class="config-chrome-flow">
          @for (child of children(); track child.id) { <shared-ui-node [node]="child" /> }
        </div>
      }
      @case (types.Step) {
        <div class="config-chrome-step">
          @if (title(); as text) { <h2 class="config-chrome-title">{{ text }}</h2> }
          @if (description(); as text) { <p class="config-chrome-description">{{ text }}</p> }
          @for (child of children(); track child.id) { <shared-ui-node [node]="child" /> }
        </div>
      }
      @case (types.Stack) {
        <div
          class="config-chrome-stack"
          [class.config-chrome-stack-accent]="accent() !== undefined && !card()"
          [class.config-chrome-stack-card]="card()"
          [class.config-chrome-stack-card-accent]="card() && accent() !== undefined"
          [attr.data-align]="stackAlign()"
          [attr.data-gap]="stackGap()"
          [style.--config-chrome-stack-accent]="accent()"
          [class.config-chrome-stack-row]="direction() === 'horizontal'"
          [class.config-chrome-stack-nowrap]="!wrap()"
          [class.config-chrome-stack-captioned]="!wrap() && hasCaptionedInput()">
          @for (child of children(); track child.id) {
            <shared-ui-node [node]="child" [style.flex]="childFlex(child)" [weighted]="direction() === 'horizontal' && childFlex(child) !== null" />
          }
        </div>
      }
      @case (types.Tabs) {
        <div class="config-chrome-tabs">
          <shared-segmented-control
            [options]="tabOptions()"
            [value]="activeTabId()"
            [stretch]="true"
            (valueChange)="onTabChange($event)" />
          @for (child of activeTabChildren(); track child.id) { <shared-ui-node [node]="child" /> }
        </div>
      }
      @case (types.Tab) {
        <div class="config-chrome-tab">
          @for (child of children(); track child.id) { <shared-ui-node [node]="child" /> }
        </div>
      }
      @case (types.Heading) {
        <h3 class="config-chrome-heading">{{ text() }}@if (badge(); as tag) {<span class="config-chrome-tag">{{ tag }}</span>}</h3>
      }
      @case (types.Prose) {
        <p
          class="config-chrome-prose"
          [class.config-chrome-prose-styled]="proseSpans() !== null || variant() !== undefined"
          [attr.data-severity]="severity()"
          [attr.data-variant]="variant()"
          [attr.role]="headingLevel() ? 'heading' : null"
          [attr.aria-level]="headingLevel()"
          >@if (proseSpans(); as runs) {@for (run of runs; track $index) {@if (run.kind === 'text') {<span
          [style]="spanStyle(run)">{{ run.text }}</span>} @else if (run.alt) {<span>{{ run.alt }}</span>}}} @else {<ng-container>{{ text() }}</ng-container>}</p
        >
      }
      @case (types.Divider) {
        <hr class="config-chrome-divider" />
      }
      @case (types.Instructions) {
        <ol class="config-chrome-instructions">
          @for (child of children(); track child.id) { <shared-ui-node [node]="child" /> }
        </ol>
      }
      @case (types.Instruction) {
        <li class="config-chrome-instruction">
          @if (text(); as t) { <span>{{ t }}</span> }
          @for (child of children(); track child.id) { <shared-ui-node [node]="child" /> }
        </li>
      }
      @case (types.CopyValue) {
        <shared-copy-value [label]="label() ?? ''" [value]="copyValue() ?? ''" />
      }
      @case (types.Link) {
        <a
          class="config-chrome-link"
          [href]="url()"
          target="_blank"
          rel="noopener noreferrer"
          (click)="onLinkClick($event)"
          (auxclick)="onLinkClick($event)"
          >{{ label() ?? url() }}</a
        >
      }
      @case (types.AdvancedSection) {
        <div class="config-chrome-advanced">
          <shared-toggle-switch
            [label]="advancedSectionLabel()"
            [checked]="expanded()"
            (changed)="onToggleExpanded($event)" />
          @if (expanded()) {
            @for (child of children(); track child.id) { <shared-ui-node [node]="child" /> }
          }
        </div>
      }
      @case (types.Banner) {
        <div class="config-chrome-banner" [attr.data-severity]="severity()">{{ text() }}</div>
      }
      @case (types.ValidationMessage) {
        @if (!hoisted()) {
          <div class="config-node-error" [id]="node().id">{{ text() }}</div>
        }
      }
      @case (types.Busy) {
        <div class="config-chrome-busy">
          <span class="config-chrome-spinner" aria-hidden="true"></span>
          @if (text(); as t) { <span>{{ t }}</span> }
        </div>
      }
      @case (types.Button) {
        @if (buttonIcon() && iconWithLabel()) {
          <shared-button [variant]="buttonVariant()" size="compact" (click)="requestActivate(node())">
            <span class="icon icon-{{ buttonIcon() }} icon-xs" aria-hidden="true"></span>
            {{ label() ?? '' }}
          </shared-button>
        } @else if (buttonIcon() && declaredVariant()) {
          <shared-button
            [variant]="buttonVariant()"
            size="icon"
            [title]="label() ?? ''"
            [ariaLabel]="label() ?? ''"
            (click)="requestActivate(node())">
            <span class="icon icon-{{ buttonIcon() }} icon-sm" aria-hidden="true"></span>
          </shared-button>
        } @else if (buttonIcon(); as icon) {
          <button
            type="button"
            class="config-chrome-icon-button"
            [title]="label() ?? ''"
            [attr.aria-label]="label() ?? ''"
            (click)="requestActivate(node())">
            <span class="icon icon-{{ icon }} icon-xs" aria-hidden="true"></span>
          </button>
        } @else {
          <shared-button [variant]="buttonVariant()" size="compact" (click)="requestActivate(node())">{{ label() ?? '' }}</shared-button>
        }
      }
      @case (types.Status) {
        <div class="config-chrome-status">
          @if (buttonIcon(); as icon) {
            <span class="icon icon-{{ icon }} icon-sm config-chrome-status-icon" aria-hidden="true"></span>
          }
          <span class="config-chrome-status-copy">{{ label() }} <strong>{{ statusValue() }}</strong></span>
          @for (child of children(); track child.id) {
            @if (child.type === types.Button && iconOf(child); as icon) {
              <shared-button
                variant="ghost"
                size="icon-compact"
                [title]="labelOf(child)"
                [attr.aria-label]="labelOf(child)"
                (click)="requestActivate(child)">
                <span class="icon icon-{{ icon }} icon-xs" aria-hidden="true"></span>
              </shared-button>
            } @else {
              <shared-ui-node [node]="child" />
            }
          }
        </div>
      }
      @case (types.Menu) {
        <shared-dropdown-menu
          position="bottom-right"
          [minWidth]="180"
          [closeOnItemClick]="true"
          [triggerIcon]="'icon-' + (buttonIcon() ?? 'dots-vertical')"
          [triggerAriaLabel]="label() ?? ''">
          <ng-template #menuContent>
            <div class="config-chrome-menu">
              @for (child of children(); track child.id) {
                @if (child.type === types.Button) {
                  <button
                    type="button"
                    class="config-chrome-menu-item"
                    [class.is-danger]="isDangerButton(child)"
                    (click)="requestActivate(child)">
                    @if (iconOf(child); as icon) {
                      <span class="icon icon-{{ icon }} icon-sm" aria-hidden="true"></span>
                    }
                    <span class="config-chrome-menu-label">{{ labelOf(child) }}</span>
                  </button>
                } @else {
                  <shared-ui-node [node]="child" />
                }
              }
            </div>
          </ng-template>
        </shared-dropdown-menu>
      }
      @case (types.Dialog) {
        <shared-modal [heading]="title() ?? ''" [showFooter]="true" (close)="cancelDialog()">
          @if (text(); as message) {
            <p class="config-chrome-dialog-message">{{ message }}</p>
          }
          @for (child of dialogBody(); track child.id) { <shared-ui-node [node]="child" /> }
          <div modal-footer>
            @for (answer of dialogAnswers(); track answer.id; let last = $last) {
              <shared-button
                [variant]="isDangerButton(answer) ? 'danger' : last ? 'primary' : 'secondary'"
                (click)="requestActivate(answer)">{{ labelOf(answer) }}</shared-button>
            }
          </div>
        </shared-modal>
      }
      @case (types.Split) {
        <shared-rail-page class="config-chrome-split" [railTitle]="railTitle()">
          <div rail-actions class="config-chrome-split-actions">
            @for (child of splitRegion(0); track child.id) { <shared-ui-node [node]="child" /> }
          </div>
          <div rail class="config-chrome-split-rail">
            @for (child of splitRegion(1); track child.id) { <shared-ui-node [node]="child" /> }
          </div>
          <div class="config-chrome-split-detail">
            @for (child of splitRegion(2); track child.id) { <shared-ui-node [node]="child" /> }
          </div>
        </shared-rail-page>
      }
      @case (types.List) {
        <div class="config-chrome-list" role="listbox" [attr.aria-label]="label() ?? null">
          @for (child of children(); track child.id) { <shared-ui-node [node]="child" /> }
        </div>
      }
      @case (types.ListItem) {
        <div class="config-chrome-list-item" [class.active]="selected()">
          <button
            type="button"
            class="config-chrome-list-item-main"
            role="option"
            [class.active]="selected()"
            [attr.aria-selected]="selected()"
            (click)="requestActivate(node())">
            @if (buttonIcon(); as icon) {
              <span class="icon icon-{{ icon }} icon-sm config-chrome-list-item-icon" aria-hidden="true"></span>
            }
            <span class="config-chrome-list-item-text">
              <span class="config-chrome-list-item-label" [title]="label() ?? ''">{{ label() }}</span>
              @if (description(); as text) {
                <span class="config-chrome-list-item-description" [title]="text">{{ text }}</span>
              }
            </span>
            @if (badge(); as tag) {
              <span class="config-chrome-tag">{{ tag }}</span>
            }
            @if (listItemSeverity(); as level) {
              <span class="config-chrome-list-item-dot" [attr.data-severity]="level" aria-hidden="true"></span>
            }
          </button>
          @if (children().length > 0) {
            <div class="config-chrome-list-item-trailing">
              @for (child of children(); track child.id) { <shared-ui-node [node]="child" /> }
            </div>
          }
        </div>
      }
      @case (types.EmptyState) {
        <shared-empty-state [icon]="buttonIcon() ?? ''" [heading]="title() ?? ''" [message]="text() ?? ''">
          @if (children().length > 0) {
            <div class="config-chrome-empty-state-actions">
              @for (child of children(); track child.id) { <shared-ui-node [node]="child" /> }
            </div>
          }
        </shared-empty-state>
      }
      @case (types.WidgetConfiguration) {
        <div class="config-chrome-widget-configuration">
          @for (child of children(); track child.id) { <shared-ui-node [node]="child" /> }
        </div>
      }
      @case (types.WidgetProperties) {
        <div class="config-chrome-widget-properties">
          @for (child of children(); track child.id) { <shared-ui-node [node]="child" /> }
        </div>
      }
      @case (types.WidgetEditor) {
        <div class="config-chrome-widget-editor">
          @for (child of children(); track child.id) { <shared-ui-node [node]="child" /> }
        </div>
      }
    }
    @if (pending(); as asking) {
      <shared-confirmation-modal
        [heading]="textOf(asking.node, properties.ConfirmTitle) ?? labelOf(asking.node)"
        [message]="textOf(asking.node, properties.ConfirmMessage) ?? ''"
        [confirmText]="textOf(asking.node, properties.ConfirmLabel) ?? labelOf(asking.node)"
        [danger]="isDanger(asking.node)"
        (confirm)="confirmPending()"
        (cancel)="pending.set(null)">
        @if (asking.prompt) {
          <shared-input
            [inputId]="asking.node.id + '-prompt'"
            [inputName]="asking.node.id + '-prompt'"
            [placeholder]="textOf(asking.node, properties.Placeholder) ?? ''"
            [ngModel]="asking.text"
            (ngModelChange)="pending.set({ node: asking.node, prompt: true, text: $event })" />
        }
      </shared-confirmation-modal>
    }
  `,
  styleUrls: ['./ui-render.component.scss', './ui-chrome-styled.component.scss', './ui-chrome-layout.component.scss'],
})
export class UiChromeComponent {
  readonly node = input.required<UiNode>();

  protected readonly types = Primitives;
  protected readonly context = inject(UiRenderContext);
  private readonly localization = inject(LocalizationService);
  private readonly externalLinks = inject(ExternalLinkService);

  protected readonly children = computed(() => this.node().children ?? []);
  protected readonly title = computed(() => nodeText(this.node(), Properties.Title, this.localization));
  protected readonly description = computed(() => nodeText(this.node(), Properties.Description, this.localization));
  protected readonly text = computed(() => nodeText(this.node(), Properties.Text, this.localization));
  protected readonly label = computed(() => nodeText(this.node(), Properties.Label, this.localization));
  protected readonly advancedSectionLabel = computed(
    () => this.label() ?? this.localization.translateKey(AppStrings.UiRender.AdvancedConfiguration),
  );
  protected readonly copyValue = computed(() => nodeString(this.node(), Properties.Value));
  protected readonly url = computed(() => nodeString(this.node(), Properties.Url) ?? '');
  protected readonly severity = computed(() => nodeString(this.node(), Properties.Severity));
  protected readonly direction = computed(() => nodeString(this.node(), Properties.Direction));
  protected readonly accent = computed(() => {
    const accent = nodeString(this.node(), Properties.Accent);
    return accent !== undefined && HexColor.test(accent) ? accent : undefined;
  });
  protected readonly variant = computed(() => nodeString(this.node(), Properties.Variant));
  protected readonly card = computed(() => this.variant() === UiStackVariants.Card);
  protected readonly stackAlign = computed(() => {
    const align = nodeString(this.node(), Properties.Align);
    return align !== undefined && StackAlignments.includes(align) ? align : null;
  });
  protected readonly stackGap = computed(() => {
    const gap = nodeString(this.node(), Properties.Gap);
    return gap !== undefined && StackGaps.includes(gap) ? gap : null;
  });
  protected readonly declaredVariant = computed(() => {
    const variant = this.variant();
    return variant !== undefined && ButtonVariants.includes(variant) ? variant as ButtonVariant : null;
  });
  protected readonly buttonVariant = computed<ButtonVariant>(() => this.declaredVariant() ?? 'secondary');
  protected readonly iconWithLabel = computed(() => nodeBoolean(this.node(), Properties.IconWithLabel) === true);
  protected readonly badge = computed(() => nodeText(this.node(), Properties.Badge, this.localization) || null);
  protected readonly selected = computed(() => nodeBoolean(this.node(), Properties.Selected) === true);
  protected readonly railTitle = computed(() => nodeText(this.node(), Properties.RailTitle, this.localization) ?? '');
  protected readonly listItemSeverity = computed(() => {
    const severity = this.severity();
    if (severity === undefined) return null;
    return ListItemSeverities.includes(severity) ? severity : 'neutral';
  });

  protected splitRegion(index: number): UiNode[] {
    const children = this.children();
    const suffix = `.${SplitRegionOrder[index]}`;
    const region = children.find(child => child.id.endsWith(suffix)) ?? children[index];
    return region?.children ?? [];
  }
  protected readonly headingLevel = computed(() => HeadingLevels[this.variant() ?? ''] ?? null);
  protected readonly proseSpans = computed(() => {
    const runs = nodeTextSpans(this.node());
    return runs !== null && runs.length > 0 ? runs : null;
  });

  protected spanStyle(run: UiTextSpanRun): Record<string, string> {
    return run.kind === 'text' ? textSpanStyle(run) : {};
  }
  protected readonly wrap = computed(() => nodeBoolean(this.node(), Properties.Wrap) !== false);
  protected readonly hasCaptionedInput = computed(() => this.children().some(child =>
    !UI_CHROME_TYPES.has(child.type) && !!nodeRaw(child, Properties.Label)
    && nodeBoolean(child, Properties.HideLabel) !== true));

  protected childFlex(child: UiNode): string | null {
    if (this.wrap()) return null;
    const weight = nodeNumber(child, Properties.RowWeight);
    return weight != null ? `${weight} ${weight} 0%` : null;
  }
  protected readonly hoisted = computed(() => {
    const forId = nodeString(this.node(), Properties.For);
    return !!forId && this.context.isHoistedOnto(forId);
  });

  private readonly expandedSignal = signal<boolean | null>(null);
  protected readonly expanded = computed(
    () => this.expandedSignal() ?? nodeBoolean(this.node(), Properties.DefaultExpanded) ?? false,
  );

  private readonly activeTabIdSignal = signal<string | null>(null);
  protected readonly tabOptions = computed<SegmentedOption[]>(() =>
    this.children().map(tab => ({ value: tab.id, label: nodeText(tab, Properties.Label, this.localization) ?? tab.id })),
  );
  protected readonly activeTabId = computed(() => this.activeTabIdSignal() ?? this.children()[0]?.id ?? null);
  protected readonly activeTab = computed(
    () => this.children().find(tab => tab.id === this.activeTabId()) ?? this.children()[0] ?? null,
  );
  protected readonly activeTabChildren = computed(() => this.activeTab()?.children ?? []);

  protected onTabChange(id: string): void {
    this.activeTabIdSignal.set(id);
  }

  protected onLinkClick(event: MouseEvent): void {
    if (event.button !== 0 && event.button !== 1) return;
    const node = this.node();
    const url = this.url();
    const external = isExternalUrl(url);
    const activates = emitsEvent(node, UiConfigEvents.Activate);
    if (!external && !activates) return;
    event.preventDefault();
    if (external) this.externalLinks.open(url);
    if (activates) this.context.emit(node, UiConfigEvents.Activate);
  }

  protected readonly buttonIcon = computed(() => nodeString(this.node(), Properties.Icon));
  protected readonly statusValue = computed(() => nodeText(this.node(), Properties.Value, this.localization));

  protected iconOf(child: UiNode): string | undefined {
    return nodeString(child, Properties.Icon);
  }

  protected labelOf(child: UiNode): string {
    return nodeText(child, Properties.Label, this.localization) ?? '';
  }

  protected readonly properties = Properties;
  protected readonly dialogAnswers = computed(() => this.children().filter(child => child.type === Primitives.Button));
  protected readonly dialogBody = computed(() => this.children().filter(child => child.type !== Primitives.Button));

  protected cancelDialog(): void {
    const node = this.node();
    if (emitsEvent(node, UiConfigEvents.Cancel)) this.context.emit(node, UiConfigEvents.Cancel);
  }
  protected readonly pending = signal<{ node: UiNode; prompt: boolean; text: string } | null>(null);

  protected textOf(node: UiNode, key: string): string | undefined {
    return nodeText(node, key, this.localization) ?? undefined;
  }

  protected isDangerButton(node: UiNode): boolean {
    return nodeString(node, Properties.Variant) === UiButtonVariants.Danger || this.isDanger(node);
  }

  protected isDanger(node: UiNode): boolean {
    return nodeBoolean(node, Properties.ConfirmDanger) === true;
  }

  protected requestActivate(node: UiNode): void {
    if (!emitsEvent(node, UiConfigEvents.Activate)) return;
    const prompt = nodeRaw(node, Properties.PromptValue) !== undefined;
    if (prompt || nodeRaw(node, Properties.ConfirmMessage) !== undefined) {
      this.pending.set({ node, prompt, text: nodeString(node, Properties.PromptValue) ?? '' });
      return;
    }
    this.context.emit(node, UiConfigEvents.Activate);
  }

  protected confirmPending(): void {
    const asking = this.pending();
    this.pending.set(null);
    if (!asking) return;
    this.context.emit(asking.node, UiConfigEvents.Activate, asking.prompt ? asking.text : undefined);
  }


  protected onToggleExpanded(open: boolean): void {
    const node = this.node();
    this.expandedSignal.set(open);
    this.context.emit(node, open ? UiConfigEvents.Expand : UiConfigEvents.Collapse);

    if (!open && nodeBoolean(node, Properties.ClearOnCollapse) === true) {
      this.context.clearDescendantInputs(node.id);
    }
  }
}
