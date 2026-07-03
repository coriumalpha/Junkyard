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
    path: 'containers',
    title: 'Contenedores · Junkyard',
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
    path: 'archive',
    title: 'Archivo · Junkyard',
    loadComponent: () => import('./archive-page.component').then((m) => m.ArchivePageComponent)
  },
  {
    path: 'cleanup/classification',
    title: 'Diagnóstico de clasificación · Junkyard',
    loadComponent: () => import('./classification-cleanup-page.component').then((m) => m.ClassificationCleanupPageComponent)
  },
  {
    path: 'settings/maintenance',
    title: 'Mantenimiento · Junkyard',
    loadComponent: () => import('./settings-maintenance-page.component').then((m) => m.SettingsMaintenancePageComponent)
  },
  {
    path: 'settings/classes',
    title: 'Clases y subtipos · Junkyard',
    loadComponent: () => import('./settings-classes-page.component').then((m) => m.SettingsClassesPageComponent)
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
