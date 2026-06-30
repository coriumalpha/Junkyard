import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'dashboard'
  },
  {
    path: 'dashboard',
    title: 'Dashboard · Junkyard',
    loadComponent: () => import('./dashboard-page.component').then((m) => m.DashboardPageComponent)
  },
  {
    path: 'inventory',
    title: 'Inventario · Junkyard',
    loadComponent: () => import('./inventory-page.component').then((m) => m.InventoryPageComponent)
  },
  {
    path: 'item/:id',
    title: 'Ítem · Junkyard',
    loadComponent: () => import('./detail-page.component').then((m) => m.DetailPageComponent)
  },
  {
    path: 'boxes/:code',
    title: 'Contenedor · Junkyard',
    loadComponent: () => import('./detail-page.component').then((m) => m.DetailPageComponent)
  },
  {
    path: 'photos/inbox',
    title: 'Fotos · Junkyard',
    loadComponent: () => import('./photo-inbox-page.component').then((m) => m.PhotoInboxPageComponent)
  },
  {
    path: 'photos/review',
    title: 'Revisión de fotos · Junkyard',
    loadComponent: () => import('./photo-review-page.component').then((m) => m.PhotoReviewPageComponent)
  },
  {
    path: 'locations',
    title: 'Ubicaciones · Junkyard',
    loadComponent: () => import('./locations-page.component').then((m) => m.LocationsPageComponent)
  },
  {
    path: 'actions',
    title: 'Pendientes · Junkyard',
    loadComponent: () => import('./actions-page.component').then((m) => m.ActionsPageComponent)
  },
  {
    path: 'settings/tags',
    title: 'Tags · Junkyard',
    loadComponent: () => import('./settings-tags-page.component').then((m) => m.SettingsTagsPageComponent)
  },
  {
    path: 'settings/conditions',
    title: 'Estados · Junkyard',
    loadComponent: () => import('./settings-conditions-page.component').then((m) => m.SettingsConditionsPageComponent)
  },
  {
    path: 'settings/data',
    title: 'Datos · Junkyard',
    loadComponent: () => import('./settings-data-page.component').then((m) => m.SettingsDataPageComponent)
  },
  {
    path: '**',
    redirectTo: 'inventory'
  }
];
