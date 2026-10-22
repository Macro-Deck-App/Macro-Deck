import { ChangeDetectionStrategy, Component, OnInit, computed, effect, inject, input, signal } from '@angular/core';
import { AppStrings, MusicPlayerInstanceDto } from '@macro-deck/runtime';
import { LocalizationService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../../feedback/loading-state/loading-state.component';
import { RailItemComponent } from '../../../rail-page/rail-item.component';
import { MusicPlayerService } from '../../../../services/music-player.service';

@Component({
  selector: 'app-integration-music-panel',
  standalone: true,
  imports: [EmptyStateComponent, LoadingStateComponent, RailItemComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './integration-music-panel.component.html',
  styleUrls: ['./capability-panel.scss', './integration-music-panel.component.scss'],
})
export class IntegrationMusicPanelComponent implements OnInit {
  readonly integrationId = input.required<string>();
  readonly available = input(true);

  private readonly music = inject(MusicPlayerService);
  private readonly localization = inject(LocalizationService);

  protected readonly loading = signal(true);

  protected readonly players = computed<MusicPlayerInstanceDto[]>(() =>
    this.music.instances().filter(instance => instance.integrationId === this.integrationId()));

  private readonly selectedId = signal<string | null>(null);

  protected readonly player = computed<MusicPlayerInstanceDto | null>(() => {
    const players = this.players();
    return players.find(player => player.instanceId === this.selectedId()) ?? players[0] ?? null;
  });

  protected readonly state = computed(() => {
    const player = this.player();
    return player ? this.music.stateFor(player.instanceId) : null;
  });

  protected readonly artwork = computed(() => {
    const player = this.player();
    const state = this.state();
    return player && state?.artworkId ? this.music.artworkUrl(player.instanceId, state.artworkId, 160) : null;
  });

  constructor() {
    effect(() => {
      const player = this.player();
      if (player) {
        void this.music.ensureState(player.instanceId);
      }
    });
  }

  async ngOnInit(): Promise<void> {
    this.music.start();
    await this.music.loadInstances();
    this.loading.set(false);
  }

  protected select(player: MusicPlayerInstanceDto): void {
    this.selectedId.set(player.instanceId);
  }

  protected position(positionMs: number | undefined, durationMs: number | undefined): string | null {
    if (positionMs === undefined || positionMs === null) {
      return null;
    }
    return durationMs ? `${clock(positionMs)} / ${clock(durationMs)}` : clock(positionMs);
  }

  protected volume(value: number | undefined): string | null {
    return value === undefined || value === null
      ? null
      : this.localization.translateKey(AppStrings.Integrations.Detail.Music.VolumeValue, { volume: Math.round(value) });
  }
}

function clock(milliseconds: number): string {
  const total = Math.max(0, Math.floor(milliseconds / 1000));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = (total % 60).toString().padStart(2, '0');
  return hours > 0 ? `${hours}:${minutes.toString().padStart(2, '0')}:${seconds}` : `${minutes}:${seconds}`;
}
