import { RoutesService, eLayoutType } from '@abp/ng.core';
import { inject, provideAppInitializer } from '@angular/core';

export const APP_ROUTE_PROVIDER = [
  provideAppInitializer(() => {
    configureRoutes();
  }),
];

function configureRoutes() {
  const routes = inject(RoutesService);
  routes.add([
    {
      path: '/',
      name: '::Menu:Home',
      iconClass: 'fas fa-home',
      order: 1,
      layout: eLayoutType.application,
    },
    {
      path: '/marketplaces',
      name: '::Menu:Marketplaces',
      iconClass: 'fas fa-store',
      order: 10,
      layout: eLayoutType.application,
    },
    {
      path: '/marketplaces/cimri/products',
      name: '::Menu:MarketplacesCimri',
      parentName: '::Menu:Marketplaces',
      layout: eLayoutType.application,
      iconClass: 'fas fa-tags',
      order: 1,
      requiredPolicy: 'BeeBAK.Cimri',
    },
    {
      path: '/marketplaces/hepsiburada',
      name: '::Menu:MarketplacesHepsiburada',
      parentName: '::Menu:Marketplaces',
      layout: eLayoutType.application,
      iconClass: 'fas fa-truck',
      order: 3,
      requiredPolicy: 'BeeBAK.Hepsiburada',
    },
    {
      path: '/marketplaces/akakce/products',
      name: '::Menu:MarketplacesAkakce',
      parentName: '::Menu:Marketplaces',
      layout: eLayoutType.application,
      iconClass: 'fas fa-chart-line',
      order: 2,
      requiredPolicy: 'BeeBAK.Akakce',
    },
  ]);
}
