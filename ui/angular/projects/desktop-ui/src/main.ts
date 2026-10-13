import { bootstrapApplication } from '@angular/platform-browser';
import { disablePageZoom } from '@macro-deck/runtime';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';
import { watchOverlayScrollbars } from './app/shared/util/overlay-scrollbars';

disablePageZoom();
watchOverlayScrollbars();

bootstrapApplication(AppComponent, appConfig)
  .catch((err) => console.error(err));
