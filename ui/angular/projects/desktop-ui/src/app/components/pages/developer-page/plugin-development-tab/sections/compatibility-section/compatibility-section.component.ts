import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { CompatibilityFinding, CompatibilityFindingSource, PluginCompatibilityReport, PluginCompatibilityState } from '@macro-deck/runtime';
import { ErrorBannerComponent, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../../../../feedback/empty-state/empty-state.component';
import { ExternalLinkService } from '../../../../../../services/external-link.service';
import { PluginCompatibilityService } from '../../../../../../services/plugin-compatibility.service';
import { compatibilityEvidenceText, compatibilityFindingSourceLabel, compatibilityStateLabel, compatibilityStateTier } from '../../../../../../util/plugin-compatibility-display';

@Component({
  selector: 'app-compatibility-section',
  standalone: true,
  imports: [EmptyStateComponent, ErrorBannerComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './compatibility-section.component.html',
  styleUrls: ['./compatibility-section.component.scss'],
})
export class CompatibilitySectionComponent {
  protected readonly compatibilityService = inject(PluginCompatibilityService);
  private readonly externalLinks = inject(ExternalLinkService);

  readonly reports = computed(() =>
    [...this.compatibilityService.reports()].sort((a, b) => a.displayName.localeCompare(b.displayName)));

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

  hasFindings(report: PluginCompatibilityReport): boolean {
    return report.findings.length > 0;
  }

  trackFinding(_index: number, finding: CompatibilityFinding): string {
    return finding.diagnosticId + finding.subject;
  }

  openMigrationLink(url: string): void {
    this.externalLinks.open(url);
  }
}
