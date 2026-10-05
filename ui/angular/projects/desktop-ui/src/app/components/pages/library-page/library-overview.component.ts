import { ChangeDetectionStrategy, Component, OnInit, Signal, computed, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AppStrings } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, TranslatePipe } from '@shared';
import {
  FONTS_CONTENT_TYPE,
  ICON_PACKS_CONTENT_TYPE,
  LIBRARY_CONTENT_TYPES,
  LibraryContentType,
} from '../../../domain/library-content-type';
import { IconPackService } from '../../../services/icon-pack.service';
import { UserFontService } from '../../../services/user-font.service';

interface LibraryCard {
  type: LibraryContentType;
  label: string;
  description: string;
  summary: string | null;
}

@Component({
  selector: 'app-library-overview',
  standalone: true,
  imports: [ButtonComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './library-overview.component.html',
  styleUrls: ['./library-overview.component.scss'],
})
export class LibraryOverviewComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly localization = inject(LocalizationService);
  private readonly iconPacks = inject(IconPackService);
  private readonly userFonts = inject(UserFontService);
  private readonly contentTypes = inject(LIBRARY_CONTENT_TYPES);

  private readonly summaries: Record<string, Signal<string>> = {
    [ICON_PACKS_CONTENT_TYPE]: computed(() => this.localization.translateKey(
      AppStrings.Library.Page.PackCount, { count: this.iconPacks.packs().length })),
    [FONTS_CONTENT_TYPE]: computed(() => this.localization.translateKey(
      AppStrings.Library.Page.FontCount, { count: this.userFonts.fonts().length })),
  };

  protected readonly cards = computed<LibraryCard[]>(() => this.contentTypes.map(type => ({
    type,
    label: this.localization.translateKey(type.labelKey),
    description: this.localization.translateKey(type.descriptionKey),
    summary: this.summaries[type.id]?.() ?? null,
  })));

  ngOnInit(): void {
    // The counts are this page's own; nothing else has necessarily loaded the packs or fonts yet.
    void this.iconPacks.loadPacks();
    void this.userFonts.load();
  }

  protected open(type: LibraryContentType): void {
    void this.router.navigateByUrl(type.route);
  }

  protected browseStore(): void {
    void this.router.navigate(['/store']);
  }
}
