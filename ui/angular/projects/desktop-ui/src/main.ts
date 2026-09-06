import { bootstrapApplication } from '@angular/platform-browser';
import { disablePageZoom } from '@macro-deck/runtime';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';

disablePageZoom();

bootstrapApplication(AppComponent, appConfig)
  .catch((err) => console.error(err));
