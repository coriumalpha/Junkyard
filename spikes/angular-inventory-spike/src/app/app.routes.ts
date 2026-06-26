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
    path: '**',
    redirectTo: 'inventory'
  }
];
