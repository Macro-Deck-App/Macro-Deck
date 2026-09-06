import { Routes } from '@angular/router';

import { unsavedChangesGuard } from './guards';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./components/shell').then(m => m.ShellComponent),
    children: [
      {
        path: '',
        redirectTo: 'deck',
        pathMatch: 'full'
      },
      {
        path: 'deck',
        loadComponent: () => import('./components/pages').then(m => m.DeckPageComponent)
      },
      {
        path: 'deck/widgets/:widgetId',
        loadComponent: () => import('./components/pages').then(m => m.WidgetEditorPageComponent),
        canDeactivate: [unsavedChangesGuard]
      },
      {
        path: 'scripts',
        loadComponent: () => import('./components/pages').then(m => m.ScriptsPageComponent),
        canDeactivate: [unsavedChangesGuard]
      },
      {
        path: 'automations',
        loadComponent: () => import('./components/pages').then(m => m.AutomationsPageComponent)
      },
      {
        path: 'integrations',
        loadComponent: () => import('./components/pages').then(m => m.IntegrationsPageComponent)
      },
      {
        path: 'integrations/:integrationId',
        loadComponent: () => import('./components/pages').then(m => m.IntegrationDetailPageComponent)
      },
      {
        path: 'library',
        loadComponent: () => import('./components/pages').then(m => m.LibraryPageComponent),
        children: [
          {
            path: '',
            loadComponent: () => import('./components/pages').then(m => m.LibraryOverviewComponent)
          },
          {
            path: 'icon-packs',
            loadComponent: () => import('./components/pages').then(m => m.IconPacksPageComponent)
          }
        ]
      },
      {
        path: 'icon-packs',
        redirectTo: 'library/icon-packs',
        pathMatch: 'full'
      },
      {
        path: 'store',
        loadComponent: () => import('./components/pages').then(m => m.StorePageComponent)
      },
      {
        path: 'store/:kind/:extensionId',
        loadComponent: () => import('./components/pages').then(m => m.StoreDetailPageComponent)
      },
      {
        path: 'variables',
        loadComponent: () => import('./components/pages').then(m => m.VariablesPageComponent)
      },
      {
        path: 'developer',
        loadComponent: () => import('./components/pages').then(m => m.DeveloperPageComponent)
      }
    ]
  }
];
