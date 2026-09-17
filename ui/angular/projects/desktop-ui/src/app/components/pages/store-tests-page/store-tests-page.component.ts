import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslatePipe } from '@shared';
import { StoreViewSwitcherComponent } from '../../store/store-view-switcher.component';
import { StoreTestsTabComponent } from './store-tests-tab.component';

// Not behind the store coming-soon gate: an invited tester needs the StoreTester role for nothing here.
@Component({
  selector: 'app-store-tests-page',
  standalone: true,
  imports: [StoreTestsTabComponent, StoreViewSwitcherComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-tests-page.component.html',
  styleUrls: ['../store-page/store-page.component.scss'],
})
export class StoreTestsPageComponent {}
