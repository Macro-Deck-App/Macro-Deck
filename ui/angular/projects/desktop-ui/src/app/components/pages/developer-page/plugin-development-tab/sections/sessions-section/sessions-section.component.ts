import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { AppStrings, PluginSessionInfo } from '@macro-deck/runtime';
import { ButtonComponent, ErrorBannerComponent, LocalizationService, ToastService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../../../../feedback/empty-state/empty-state.component';
import { ConfirmationModalComponent } from '../../../../../overlay/confirmation-modal/confirmation-modal.component';
import { PluginTokenService } from '../../../../../../services/plugin-token.service';

@Component({
  selector: 'app-sessions-section',
  standalone: true,
  imports: [
    ButtonComponent,
    ConfirmationModalComponent,
    EmptyStateComponent,
    ErrorBannerComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sessions-section.component.html',
  styleUrls: ['./sessions-section.component.scss'],
})
export class SessionsSectionComponent {
  protected readonly tokenService = inject(PluginTokenService);
  private readonly toastService = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  readonly sessions = computed(() =>
    this.tokenService.sessions()
      .filter(session => session.origin === 'self-registered')
      .sort((a, b) => b.connectedAt.localeCompare(a.connectedAt)));

  readonly terminateCandidate = signal<PluginSessionInfo | null>(null);

  displayName(session: PluginSessionInfo): string {
    return session.displayName?.trim() || session.pluginId;
  }

  isOnline(session: PluginSessionInfo): boolean {
    return session.state === 'connected';
  }

  requestTerminate(session: PluginSessionInfo): void {
    this.terminateCandidate.set(session);
  }

  cancelTerminate(): void {
    this.terminateCandidate.set(null);
  }

  async confirmTerminate(): Promise<void> {
    const session = this.terminateCandidate();
    if (!session) {
      return;
    }
    this.terminateCandidate.set(null);

    const response = await this.tokenService.terminateSession(session.sessionId);
    if (!response.success) {
      this.toastService.show(
        response.error?.message
          ?? this.localization.translateKey(AppStrings.Developer.PluginDevelopment.Sessions.TerminateFailed),
        { variant: 'error' });
    }
  }

  formatDate(iso: string | null | undefined): string {
    if (!iso) {
      return '-';
    }
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) {
      return '-';
    }
    return date.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
  }
}
