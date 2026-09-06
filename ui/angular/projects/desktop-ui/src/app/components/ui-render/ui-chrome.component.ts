import { ChangeDetectionStrategy, Component, computed, forwardRef, inject, input, signal } from '@angular/core';

import { AppStrings, UiConfigEvents, UiConfigPrimitives, UiConfigProperties, UiNode, emitsEvent, nodeBoolean, nodeNumber, nodeString, nodeText } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, SegmentedControlComponent, SegmentedOption, ToggleSwitchComponent } from '@shared';
import { CopyValueComponent } from '../copy-value/copy-value.component';
import { UiRenderContext } from './ui-render-context';
import { UiNodeComponent } from './ui-node.component';

const Primitives = UiConfigPrimitives;
const Properties = UiConfigProperties;

@Component({
  selector: 'shared-ui-chrome',
  standalone: true,
  imports: [
    forwardRef(() => UiNodeComponent),
    ButtonComponent,
    CopyValueComponent,
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
          [class.config-chrome-stack-row]="direction() === 'horizontal'"
          [class.config-chrome-stack-nowrap]="!wrap()">
          @for (child of children(); track child.id) {
            <shared-ui-node [node]="child" [style.flex]="childFlex(child)" />
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
        <h3 class="config-chrome-heading">{{ text() }}</h3>
      }
      @case (types.Prose) {
        <p class="config-chrome-prose" [attr.data-severity]="severity()">{{ text() }}</p>
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
        @if (buttonIcon(); as icon) {
          <shared-button
            variant="secondary"
            size="icon"
            class="config-chrome-icon-button"
            [title]="label() ?? ''"
            [attr.aria-label]="label() ?? ''"
            (click)="onActivate()">
            <span class="icon icon-{{ icon }} icon-sm" aria-hidden="true"></span>
          </shared-button>
        } @else {
          <shared-button variant="secondary" size="compact" (click)="onActivate()">{{ label() ?? '' }}</shared-button>
        }
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
  `,
  styleUrls: ['./ui-render.component.scss'],
})
export class UiChromeComponent {
  readonly node = input.required<UiNode>();

  protected readonly types = Primitives;
  protected readonly context = inject(UiRenderContext);
  private readonly localization = inject(LocalizationService);

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
  protected readonly wrap = computed(() => nodeBoolean(this.node(), Properties.Wrap) !== false);

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
    const node = this.node();
    if (!emitsEvent(node, UiConfigEvents.Activate)) return;
    event.preventDefault();
    this.context.emit(node, UiConfigEvents.Activate);
  }

  protected readonly buttonIcon = computed(() => nodeString(this.node(), Properties.Icon));

  protected onActivate(): void {
    const node = this.node();
    if (!emitsEvent(node, UiConfigEvents.Activate)) return;
    this.context.emit(node, UiConfigEvents.Activate);
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
