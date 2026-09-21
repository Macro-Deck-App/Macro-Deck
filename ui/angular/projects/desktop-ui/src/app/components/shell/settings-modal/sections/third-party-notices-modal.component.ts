import { ChangeDetectionStrategy, Component, ViewChild, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AppStrings,
  AppStringsKey,
  GetThirdPartyNoticesResponse,
  ThirdPartyComponent,
  ThirdPartyComponentEcosystem,
} from '@macro-deck/runtime';
import { ApiService, ButtonComponent, InputComponent, ModalComponent, TranslatePipe, dismissModal } from '@shared';
import { ExternalLinkService } from '../../../../services/external-link.service';

interface NoticeRow {
  key: string;
  component: ThirdPartyComponent;
  licenseLabel: string;
  platformsLabel: string | null;
  hasDetails: boolean;
}

interface NoticeGroup {
  ecosystem: ThirdPartyComponentEcosystem;
  headingKey: AppStringsKey;
  rows: NoticeRow[];
}

const ECOSYSTEMS: ReadonlyArray<{ ecosystem: ThirdPartyComponentEcosystem; headingKey: AppStringsKey }> = [
  { ecosystem: 'nuget', headingKey: AppStrings.Settings.About.Licenses.Group.NuGet },
  { ecosystem: 'npm', headingKey: AppStrings.Settings.About.Licenses.Group.Npm },
  { ecosystem: 'cargo', headingKey: AppStrings.Settings.About.Licenses.Group.Cargo },
  { ecosystem: 'asset', headingKey: AppStrings.Settings.About.Licenses.Group.Asset },
];

@Component({
  selector: 'app-third-party-notices-modal',
  standalone: true,
  imports: [FormsModule, ModalComponent, ButtonComponent, InputComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './third-party-notices-modal.component.html',
  styleUrls: ['./third-party-notices-modal.component.scss'],
})
export class ThirdPartyNoticesModalComponent {
  private readonly api = inject(ApiService);
  private readonly externalLinks = inject(ExternalLinkService);

  readonly showAppImageNote = input(false);
  readonly closed = output<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  protected readonly strings = AppStrings.Settings.About.Licenses;

  readonly notices = signal<GetThirdPartyNoticesResponse | null>(null);
  readonly loading = signal(false);
  readonly loadFailed = signal(false);
  readonly query = signal('');
  private readonly expanded = signal<ReadonlySet<string>>(new Set());

  private readonly textsById = computed(() => {
    const map = new Map<number, string>();
    for (const text of this.notices()?.texts ?? []) {
      map.set(text.id, text.content);
    }
    return map;
  });

  private readonly rows = computed<NoticeRow[]>(() =>
    (this.notices()?.components ?? []).map((component, index) => ({
      key: `${component.ecosystem}-${index}`,
      component,
      licenseLabel: component.licenses.join(', '),
      platformsLabel: component.platforms.length > 0 ? component.platforms.join(', ') : null,
      hasDetails: component.textIds.length > 0 || !!component.note,
    })));

  readonly groups = computed<NoticeGroup[]>(() => {
    const needle = this.query().trim().toLowerCase();
    const matching = needle === ''
      ? this.rows()
      : this.rows().filter(row => this.matches(row.component, needle));
    return ECOSYSTEMS
      .map(({ ecosystem, headingKey }) => ({
        ecosystem,
        headingKey,
        rows: matching.filter(row => row.component.ecosystem === ecosystem),
      }))
      .filter(group => group.rows.length > 0);
  });

  constructor() {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailed.set(false);
    try {
      this.notices.set(await this.api.getThirdPartyNotices());
    } catch {
      this.loadFailed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  isExpanded(key: string): boolean {
    return this.expanded().has(key);
  }

  toggle(key: string): void {
    const next = new Set(this.expanded());
    if (!next.delete(key)) {
      next.add(key);
    }
    this.expanded.set(next);
  }

  textsFor(row: NoticeRow): string[] {
    const texts = this.textsById();
    return row.component.textIds
      .map(id => texts.get(id))
      .filter((text): text is string => text !== undefined);
  }

  openUrl(url: string): void {
    this.externalLinks.open(url);
  }

  close(): void {
    dismissModal(this.modal, () => this.closed.emit());
  }

  private matches(component: ThirdPartyComponent, needle: string): boolean {
    return component.name.toLowerCase().includes(needle)
      || component.licenses.some(license => license.toLowerCase().includes(needle))
      || component.declared.some(license => license.toLowerCase().includes(needle));
  }
}
