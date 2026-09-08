import { Component, input, ChangeDetectionStrategy } from '@angular/core';

@Component({
  selector: 'app-splash',
  standalone: true,
  imports: [],
  templateUrl: './splash.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./splash.component.scss']
})
export class SplashComponent {
  readonly status = input('');
}
