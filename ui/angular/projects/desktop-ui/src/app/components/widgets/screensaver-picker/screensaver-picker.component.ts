import {
  ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, model, signal, untracked,
} from '@angular/core';

import { FormsModule } from '@angular/forms';

import { AppStrings, IpcScreenSaver, UiConfigEntryPoints, UiConfigEvents, UiNode, UiNodeEvent, resolveLocalizedText } from '@macro-deck/runtime';
import { ButtonComponent, ButtonGroupComponent, LocalizationService, ModalComponent, ScreenSaverService, TranslatePipe, UiSessionHandle, UiSessionService } from '@shared';
import { SelectComponent, SelectOption } from '../../forms/select/select.component';
import { UiTreeComponent } from '../../ui-render/ui-tree.component';

@Component({
  selector: 'app-screensaver-picker',
  standalone: true,
  imports: [FormsModule, SelectComponent, TranslatePipe, UiTreeComponent, ButtonComponent, ButtonGroupComponent, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './screensaver-picker.component.html',
  styleUrls: ['./screensaver-picker.component.scss'],
})
export class ScreenSaverPickerComponent {
  readonly screenSaverId = model<string>('');

  readonly configuration = model<string | null>(null);

  readonly deviceId = input<string | null>(null);

  private readonly screenSavers = inject(ScreenSaverService);
  private readonly localization = inject(LocalizationService);
  private readonly uiSessions = inject(UiSessionService);

  protected readonly entries = this.screenSavers.screenSavers;

  protected readonly selected = computed(() => this.screenSavers.find(this.screenSaverId()));

  protected readonly showConfiguration = computed(() => this.selected()?.hasConfiguration === true);

  protected readonly root = computed<UiNode | null>(() => this.session()?.root() ?? null);

  protected readonly optionsOpen = signal(false);

  private readonly session = signal<UiSessionHandle | null>(null);
  private values: Record<string, unknown> = {};
  private openedScreenSaverId: string | null = null;

  constructor() {
    effect(() => void this.screenSavers.load());

    effect(() => {
      const screenSaverId = this.screenSaverId();
      const canConfigure = this.showConfiguration() && this.optionsOpen();

      if (!canConfigure) {
        this.closeSession();
        return;
      }

      if (screenSaverId !== this.openedScreenSaverId) untracked(() => this.openSession(screenSaverId));
    });

    inject(DestroyRef).onDestroy(() => this.closeSession());
  }

  protected readonly options = computed<SelectOption[]>(() => {
    const options = this.entries().map(entry => ({ value: entry.id, label: this.label(entry) }));
    const current = this.screenSaverId();
    // A stored selection whose provider is away stays visible rather than silently becoming the clock.
    if (current && !this.entries().some(entry => entry.id === current)) {
      options.push({
        value: current,
        label: this.localization.translateKey(AppStrings.Settings.Devices.ScreenSaver.Unavailable),
      });
    }
    return options;
  });

  protected description(entry: IpcScreenSaver): string {
    return resolveLocalizedText(entry.description, this.localization);
  }

  protected onSelect(value: string): void {
    if (value === this.screenSaverId()) return;

    this.values = {};
    this.optionsOpen.set(false);
    this.configuration.set(null);
    this.screenSaverId.set(value);
  }

  private label(entry: IpcScreenSaver): string {
    return resolveLocalizedText(entry.name, this.localization) || entry.id;
  }

  protected onNodeEvent(event: UiNodeEvent): void {
    if (event.name === UiConfigEvents.Change) {
      this.values = { ...this.values, [event.nodeId]: event.data };
      this.configuration.set(JSON.stringify(this.values));
    }

    this.session()?.send(event);
  }

  private openSession(screenSaverId: string): void {
    this.closeSession();
    this.openedScreenSaverId = screenSaverId;

    const integrationId = this.selected()?.providerId ?? '';
    if (!integrationId) return;

    this.values = this.parseConfiguration(this.configuration());

    this.session.set(this.uiSessions.open({
      kind: 'config',
      entryPoint: UiConfigEntryPoints.ScreenSaverConfig,
      integrationId,
      deviceId: this.deviceId() ?? undefined,
      screenSaverId,
      screenSaverConfiguration: this.configuration() ?? undefined,
      configUiModelVersion: 0,
    }));
  }

  private closeSession(): void {
    this.session()?.close();
    this.session.set(null);
    this.openedScreenSaverId = null;
  }

  private parseConfiguration(json: string | null): Record<string, unknown> {
    if (!json) return {};

    try {
      const parsed: unknown = JSON.parse(json);
      return parsed !== null && typeof parsed === 'object' && !Array.isArray(parsed)
        ? parsed as Record<string, unknown>
        : {};
    } catch {
      return {};
    }
  }
}
