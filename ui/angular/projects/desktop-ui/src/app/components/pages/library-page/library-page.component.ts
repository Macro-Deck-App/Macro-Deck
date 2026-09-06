import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs/operators';
import { LIBRARY_CONTENT_TYPES } from '../../../domain/library-content-type';
import {
  LIBRARY_OVERVIEW_ID,
  LibraryContentTypeSwitcherComponent,
} from '../../library/library-content-type-switcher.component';

@Component({
  selector: 'app-library-page',
  standalone: true,
  imports: [LibraryContentTypeSwitcherComponent, RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './library-page.component.html',
  styleUrls: ['./library-page.component.scss'],
})
export class LibraryPageComponent {
  private readonly router = inject(Router);
  private readonly contentTypes = inject(LIBRARY_CONTENT_TYPES);

  private readonly url = signal(this.router.url);

  protected readonly activeId = computed(() => {
    const path = this.url().split(/[?#]/)[0];
    return this.contentTypes.find(type => path === type.route || path.startsWith(`${type.route}/`))
      ?.id ?? LIBRARY_OVERVIEW_ID;
  });

  constructor() {
    this.router.events
      .pipe(filter(event => event instanceof NavigationEnd), takeUntilDestroyed())
      .subscribe(() => this.url.set(this.router.url));
  }
}
