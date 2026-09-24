import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslatePipe } from '@shared';
import { StoreCommunityNoticeComponent } from '../../store/store-community-notice.component';
import { StoreFooterComponent } from '../../store/store-footer.component';
import { StorePageHeaderComponent } from '../../store/store-page-header.component';
import { StoreTestsTabComponent } from './store-tests-tab.component';

// Not behind the store coming-soon gate: an invited tester needs the StoreTester role for nothing here.
@Component({
  selector: 'app-store-tests-page',
  standalone: true,
  imports: [StoreCommunityNoticeComponent, StoreFooterComponent, StorePageHeaderComponent, StoreTestsTabComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-tests-page.component.html',
  styleUrls: ['../store-page/store-page.component.scss'],
})
export class StoreTestsPageComponent {}
