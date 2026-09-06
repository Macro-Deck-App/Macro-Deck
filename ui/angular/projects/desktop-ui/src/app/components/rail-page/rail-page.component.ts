import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

@Component({
  selector: 'shared-rail-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-content select="[page-banner]"></ng-content>

    <div class="panes">
      <aside class="rail">
        <div class="rail-header">
          <h1>{{ railTitle }}</h1>
          <ng-content select="[rail-actions]"></ng-content>
        </div>
        <ng-content select="[rail]"></ng-content>
      </aside>

      <section class="detail">
        <ng-content></ng-content>
      </section>
    </div>
  `,
  styleUrls: ['./rail-page.component.scss'],
})
export class RailPageComponent {
  @Input({ required: true }) railTitle = '';
}
