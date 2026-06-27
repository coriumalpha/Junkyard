import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'dashboard'
  },
  {
    path: 'dashboard',
    loadComponent: () => import('./dashboard-page.component').then((m) => m.DashboardPageComponent)
  },
  {
    path: 'inventory',
    loadComponent: () => import('./inventory-page.component').then((m) => m.InventoryPageComponent)
  },
  {
    path: 'item/:id',
    loadComponent: () => import('./detail-page.component').then((m) => m.DetailPageComponent)
  },
  {
    path: 'boxes/:code',
    loadComponent: () => import('./detail-page.component').then((m) => m.DetailPageComponent)
  },
  {
    path: 'photos/inbox',
    loadComponent: () => import('./photo-inbox-page.component').then((m) => m.PhotoInboxPageComponent)
  },
  {
    path: 'photos/review',
    loadComponent: () => import('./photo-review-page.component').then((m) => m.PhotoReviewPageComponent)
  },
  {
    path: 'locations',
    loadComponent: () => import('./locations-page.component').then((m) => m.LocationsPageComponent)
  },
  {
    path: 'actions',
    loadComponent: () => import('./actions-page.component').then((m) => m.ActionsPageComponent)
  },
  {
    path: 'settings/tags',
    loadComponent: () => import('./settings-tags-page.component').then((m) => m.SettingsTagsPageComponent)
  },
  {
    path: '**',
    redirectTo: 'inventory'
  }
];
