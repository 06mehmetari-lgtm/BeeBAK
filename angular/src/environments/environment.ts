import { Environment } from '@abp/ng.core';

const baseUrl = 'http://localhost:4200';

const oAuthConfig = {
  issuer: 'https://localhost:44381/',
  redirectUri: baseUrl,
  clientId: 'BeeBAK_App',
  responseType: 'code',
  scope: 'offline_access BeeBAK',
  requireHttps: true,
};

export const environment = {
  production: false,
  application: {
    baseUrl,
    name: 'BeeBAK',
  },
  oAuthConfig,
  apis: {
    default: {
      url: 'https://localhost:44381',
      rootNamespace: 'BeeBAK',
    },
    AbpAccountPublic: {
      url: oAuthConfig.issuer,
      rootNamespace: 'AbpAccountPublic',
    },
  },
} as Environment;
