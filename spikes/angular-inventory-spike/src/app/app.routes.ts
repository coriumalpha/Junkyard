import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'inventory'
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
