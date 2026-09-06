import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { VariableCatalogNode, VariableCatalogProvider } from '@macro-deck/runtime';
import type { IntegrationSourceEntry, VariableSourceFilter } from '../../../domain/variable-source.util';
import { AppStrings, resolveLocalizedText } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, TranslatePipe, VariableService } from '@shared';
import { RailItemComponent } from '../../rail-page/rail-item.component';
import { RailPageComponent } from '../../rail-page/rail-page.component';
import { VariableBindDialogComponent } from '../../variables/variable-bind-dialog.component';
import { VariablesManagerComponent } from '../../variables/variables-manager.component';
import { integrationSourceEntries } from '../../../domain/variable-source.util';
import { VariableCatalogService } from '../../../services/variable-catalog.service';
import { IntegrationService } from '../../../services/integration.service';

@Component({
  selector: 'app-variables-page',
  standalone: true,
  imports: [
    ButtonComponent,
    RailItemComponent,
    RailPageComponent,
    VariablesManagerComponent,
    VariableBindDialogComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './variables-page.component.html',
  styleUrls: ['./variables-page.component.scss'],
})
export class VariablesPageComponent {
  private readonly localization = inject(LocalizationService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly integrationService = inject(IntegrationService);
  private readonly variableService = inject(VariableService);
  private readonly variableCatalog = inject(VariableCatalogService);

  readonly source = signal<VariableSourceFilter>({ kind: 'all' });

  readonly bindDialogNode = signal<VariableCatalogNode | null>(null);

  readonly totalCount = computed(() => this.variableService.globalVariables().length);

  readonly userCount = computed(() =>
    this.variableService.globalVariables().filter(v => v.classification === 'user').length);

  readonly catalogProviders = computed<VariableCatalogProvider[]>(() => this.variableCatalog.providersFor()());

  private readonly catalogIntegrationIds = computed<string[]>(() =>
    this.catalogProviders().map(p => p.integrationId));

  readonly integrationSources = computed<IntegrationSourceEntry[]>(() =>
    integrationSourceEntries(
      this.variableService.globalVariables(),
      id => this.integrationDisplayName(id),
      this.catalogIntegrationIds(),
    ));

  readonly catalogIntegrationId = computed<string | null>(() => {
    const source = this.source();
    if (source.kind !== 'integration') {
      return null;
    }
    return this.catalogIntegrationIds().includes(source.integrationId) ? source.integrationId : null;
  });

  readonly managerTitle = computed(() => {
    const source = this.source();
    switch (source.kind) {
      case 'all':
        return this.localization.translateKey(AppStrings.Variables.AllVariablesHeading);
      case 'user':
        return this.localization.translateKey(AppStrings.Variables.UserVariablesHeading);
      case 'integration':
        return this.integrationDisplayName(source.integrationId);
      case 'event':
      case 'scope':
        return this.localization.translateKey(AppStrings.Variables.VariablesHeading);
    }
  });

  constructor() {
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe(params => {
      const integrationId = params.get('integrationId');
      if (integrationId) {
        this.source.set({ kind: 'integration', integrationId });
      } else if (this.source().kind === 'integration') {
        this.source.set({ kind: 'all' });
      }
    });

    if (this.integrationService.integrations().length === 0) {
      void this.integrationService.loadIntegrations();
    }
    void this.variableCatalog.loadProviders();
  }

  isAllSelected(): boolean {
    return this.source().kind === 'all';
  }

  isUserSelected(): boolean {
    return this.source().kind === 'user';
  }

  isIntegrationSelected(integrationId: string): boolean {
    const source = this.source();
    return source.kind === 'integration' && source.integrationId === integrationId;
  }

  selectAll(): void {
    this.select({ kind: 'all' });
  }

  selectUser(): void {
    this.select({ kind: 'user' });
  }

  selectIntegration(integrationId: string): void {
    void this.router.navigate(['/variables'], { queryParams: { integrationId } });
  }

  catalogProviderName(integrationId: string): string {
    const provider = this.catalogProviders().find(p => p.integrationId === integrationId);
    if (!provider) return this.integrationDisplayName(integrationId);
    return resolveLocalizedText(provider.name, this.localization) || this.integrationDisplayName(integrationId);
  }

  protected readonly bindDialogIntegrationId = signal<string | null>(null);

  protected unboundCountFor(integrationId: string): number | null {
    return this.catalogIntegrationIds().includes(integrationId)
      ? this.variableCatalog.unboundCountFor(integrationId)
      : null;
  }

  onCatalogBindRequested(request: { integrationId: string; node: VariableCatalogNode }): void {
    this.bindDialogIntegrationId.set(request.integrationId);
    this.bindDialogNode.set(request.node);
  }

  onCatalogBound(): void {
    this.bindDialogNode.set(null);
  }

  closeCatalogBindDialog(): void {
    this.bindDialogNode.set(null);
  }

  private integrationDisplayName(integrationId: string): string {
    return this.integrationService.integrations().find(i => i.id === integrationId)?.name ?? integrationId;
  }

  private select(source: VariableSourceFilter): void {
    this.source.set(source);
    if (this.route.snapshot.queryParamMap.has('integrationId')) {
      void this.router.navigate(['/variables']);
    }
  }
}
