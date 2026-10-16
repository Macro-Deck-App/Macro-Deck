import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { StoreDependenciesBody, StoreDependencyPackageBody } from '@macro-deck/runtime';
import { TranslatePipe } from '@shared';
import { ExternalLinkService } from '../../../services/external-link.service';
import { packageUrl, splitPackages } from './store-dependencies';

@Component({
  selector: 'app-store-dependencies-panel',
  standalone: true,
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-dependencies-panel.component.html',
  styleUrls: ['./store-dependencies-panel.component.scss'],
})
export class StoreDependenciesPanelComponent {
  readonly dependencies = input<StoreDependenciesBody | null | undefined>(null);

  private readonly externalLinks = inject(ExternalLinkService);

  protected readonly frameworks = computed(() => (this.dependencies()?.frameworks ?? [])
    .map(framework => ({ framework: framework.framework, ...splitPackages(framework.packages) })));

  protected readonly showFrameworkNames = computed(() => this.frameworks().length > 1);

  protected packageUrl(entry: StoreDependencyPackageBody): string | null {
    const dependencies = this.dependencies();
    return dependencies ? packageUrl(dependencies, entry) : null;
  }

  protected open(event: MouseEvent, url: string): void {
    event.preventDefault();
    this.externalLinks.open(url);
  }
}
