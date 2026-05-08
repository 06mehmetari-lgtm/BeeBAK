import { authGuard, permissionGuard } from '@abp/ng.core';
import { Routes } from '@angular/router';

export const APP_ROUTES: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./home/home.component').then(c => c.HomeComponent),
  },
  {
    path: 'marketplaces',
    loadComponent: () =>
      import('./marketplaces/marketplaces-shell.component').then(m => m.MarketplacesShellComponent),
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'trendyol/products' },
      {
        path: 'trendyol/products',
        loadComponent: () =>
          import('./marketplaces/trendyol/trendyol-products.component').then(m => m.TrendyolProductsComponent),
        canActivate: [permissionGuard],
        data: { requiredPolicy: 'BeeBAK.Trendyol' },
      },
      {
        path: 'hepsiburada',
        loadComponent: () =>
          import('./marketplaces/hepsiburada/hepsiburada-page.component').then(m => m.HepsiburadaPageComponent),
        canActivate: [permissionGuard],
        data: { requiredPolicy: 'BeeBAK.Hepsiburada' },
      },
    ],
  },
  {
    path: 'account',
    loadChildren: () => import('@abp/ng.account').then(c => c.createRoutes()),
  },
  {
    path: 'identity',
    loadChildren: () => import('@abp/ng.identity').then(c => c.createRoutes()),
  },
  {
    path: 'tenant-management',
    loadChildren: () => import('@abp/ng.tenant-management').then(c => c.createRoutes()),
  },
  {
    path: 'setting-management',
    loadChildren: () => import('@abp/ng.setting-management').then(c => c.createRoutes()),
  },
];
