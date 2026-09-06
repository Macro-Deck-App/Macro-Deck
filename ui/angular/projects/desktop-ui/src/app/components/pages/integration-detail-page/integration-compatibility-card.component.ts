import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { CompatibilityFinding, CompatibilityFindingSource, PluginCompatibilityReport, PluginCompatibilityState } from '@macro-deck/runtime';
import { ButtonComponent, ErrorBannerComponent, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../feedback/loading-state/loading-state.component';
import { ExternalLinkService } from '../../../services/external-link.service';
import { compatibilityEvidenceText, compatibilityFindingSourceLabel, compatibilityStateLabel, compatibilityStateTier } from '../../../util/plugin-compatibility-display';

@Component({
  selector: 'app-integration-compatibility-card',
  standalone: true,
  imports: [ButtonComponent, EmptyStateComponent, ErrorBannerComponent, LoadingStateComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { style: 'display: contents;' },
  templateUrl: './integration-compatibility-card.component.html',
  styleUrls: ['./integration-compatibility-card.component.scss'],
})
export class IntegrationCompatibilityCardComponent {
  readonly report = input.required<PluginCompatibilityReport | null>();
  readonly loading = input(false);
  readonly error = input<string | null>(null);

  private readonly externalLinks = inject(ExternalLinkService);

  stateLabel(state: PluginCompatibilityState): string {
    return compatibilityStateLabel(state);
  }

  stateTier(state: PluginCompatibilityState): string {
    return compatibilityStateTier(state);
  }

  evidenceText(report: PluginCompatibilityReport): string {
    return compatibilityEvidenceText(report);
  }

  sourceLabel(source: CompatibilityFindingSource): string {
    return compatibilityFindingSourceLabel(source);
  }

  trackFinding(_index: number, finding: CompatibilityFinding): string {
    return finding.diagnosticId + finding.subject;
  }

  openMigrationLink(url: string): void {
    this.externalLinks.open(url);
  }
}
