import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings, IpcIntegrationCalendar, IpcIntegrationCalendarAccount, IpcIntegrationCalendarEvent } from '@macro-deck/runtime';
import { ApiService, CheckboxComponent, ErrorBannerComponent, LocalizationService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../../feedback/loading-state/loading-state.component';
import { RailItemComponent } from '../../../rail-page/rail-item.component';
import { VariableGroupHeaderComponent } from '../../../variables/variable-group-header.component';
import { VariableRowComponent } from '../../../variables/variable-row.component';
import { CapabilityDataLoader } from './capability-data-loader';

interface CalendarEventRow {
  event: IpcIntegrationCalendarEvent;
  details: string;
  calendar: IpcIntegrationCalendar | undefined;
}

interface CalendarDay {
  key: string;
  label: string;
  events: CalendarEventRow[];
}

@Component({
  selector: 'app-integration-calendars-panel',
  standalone: true,
  imports: [
    CheckboxComponent,
    EmptyStateComponent,
    ErrorBannerComponent,
    FormsModule,
    LoadingStateComponent,
    RailItemComponent,
    TranslatePipe,
    VariableGroupHeaderComponent,
    VariableRowComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './integration-calendars-panel.component.html',
  styleUrls: ['./capability-panel.scss', './integration-calendars-panel.component.scss'],
})
export class IntegrationCalendarsPanelComponent {
  readonly integrationId = input.required<string>();

  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  protected readonly loader = new CapabilityDataLoader('calendar', id => this.api.getIntegrationCalendars(id));

  protected readonly accounts = computed(() => this.loader.data()?.accounts ?? []);

  private readonly selectedAccountId = signal<string | null>(null);
  private readonly hiddenCalendars = signal<ReadonlySet<string>>(new Set());

  protected readonly account = computed<IpcIntegrationCalendarAccount | null>(() => {
    const accounts = this.accounts();
    return accounts.find(account => account.accountId === this.selectedAccountId()) ?? accounts[0] ?? null;
  });

  protected readonly window = computed(() => {
    const data = this.loader.data();
    if (!data?.windowStart || !data.windowEnd) {
      return null;
    }
    return this.localization.translateKey(AppStrings.Integrations.Detail.Calendars.SyncWindow, {
      from: this.formatDate(data.windowStart),
      to: this.formatDate(data.windowEnd),
    });
  });

  protected readonly days = computed<CalendarDay[]>(() => {
    const account = this.account();
    if (!account) {
      return [];
    }

    const hidden = this.hiddenCalendars();
    const calendars = new Map(account.calendars.map(calendar => [calendar.key, calendar] as const));
    const days = new Map<string, CalendarDay>();
    const events = (this.loader.data()?.events ?? [])
      .filter(event => event.accountId === account.accountId && !hidden.has(event.calendarKey))
      .sort((a, b) => new Date(a.start).getTime() - new Date(b.start).getTime());

    for (const event of events) {
      const start = new Date(event.start);
      const key = `${start.getFullYear()}-${start.getMonth()}-${start.getDate()}`;
      let day = days.get(key);
      if (!day) {
        day = { key, label: this.formatDay(start), events: [] };
        days.set(key, day);
      }
      day.events.push({
        event,
        details: [this.formatRange(event), event.location].filter(part => !!part).join(' · '),
        calendar: calendars.get(event.calendarKey),
      });
    }

    return [...days.values()];
  });

  constructor() {
    effect(() => {
      const id = this.integrationId();
      untracked(() => void this.loader.load(id));
    });
  }

  protected selectAccount(account: IpcIntegrationCalendarAccount): void {
    this.selectedAccountId.set(account.accountId);
  }

  protected isShown(calendar: IpcIntegrationCalendar): boolean {
    return !this.hiddenCalendars().has(calendar.key);
  }

  protected toggleCalendar(calendar: IpcIntegrationCalendar): void {
    this.hiddenCalendars.update(hidden => {
      const next = new Set(hidden);
      if (!next.delete(calendar.key)) {
        next.add(calendar.key);
      }
      return next;
    });
  }

  private formatRange(event: IpcIntegrationCalendarEvent): string {
    if (event.isAllDay) {
      return this.localization.translateKey(AppStrings.Integrations.Detail.Calendars.AllDay);
    }
    const { locale, hourCycle } = this.localization.timeLocale();
    return new Intl.DateTimeFormat(locale, { hourCycle, hour: 'numeric', minute: '2-digit' })
      .formatRange(new Date(event.start), new Date(event.end));
  }

  private formatDay(date: Date): string {
    return date.toLocaleDateString(this.localization.timeLocale().locale, { weekday: 'long', day: 'numeric', month: 'long' });
  }

  private formatDate(value: string): string {
    return new Date(value).toLocaleDateString(this.localization.timeLocale().locale, { dateStyle: 'medium' });
  }
}
