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
    path: '**',
    redirectTo: 'inventory'
  }
];
