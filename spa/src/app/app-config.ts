import { InjectionToken } from '@angular/core';

export interface AppConfig {
  authority: string;
  clientId: string;
  knownAuthorities: string[];
  protocolMode: 'OIDC' | 'AAD';
  redirectUri: string;
  scopes: string[];
  apiBaseUrl: string;
}

export const APP_CONFIG = new InjectionToken<AppConfig>('APP_CONFIG');
