import { Environment } from '@abp/ng.core';

const baseUrl = 'http://localhost:4200';

const oAuthConfig = {
  issuer: 'http://localhost:44381/',
  redirectUri: baseUrl,
  clientId: 'BeeBAK_App',
  responseType: 'code',
  scope: 'offline_access BeeBAK',
  requireHttps: false,
};

export const environment = {
  production: false,
  localization: {
    defaultResourceName: 'BeeBAK',
  },
  application: {
    baseUrl,
    name: 'BeeBAK',
  },
  oAuthConfig,
  apis: {
    default: {
      url: 'http://localhost:44381',
      rootNamespace: 'BeeBAK',
    },
    AbpAccountPublic: {
      url: oAuthConfig.issuer,
      rootNamespace: 'AbpAccountPublic',
    },
  },
} as Environment;
