import { bootstrapApplication } from '@angular/platform-browser';
import { PublicClientApplication, ProtocolMode } from '@azure/msal-browser';
import { broadcastResponseToMainFrame } from '@azure/msal-browser/redirect-bridge';
import { MSAL_INSTANCE } from '@azure/msal-angular';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { AppConfig, APP_CONFIG } from './app/app-config';

async function start() {
  // msal-browser 5.x relays silent responses to the main frame from the redirect URI loaded in the hidden iframe; never boot the app there.
  const inIframe = window.self !== window.top;
  if (inIframe && /[#?&]state=/.test(location.hash + location.search)) {
    await broadcastResponseToMainFrame();
    return;
  }

  // Provider settings are loaded at runtime so switching Keycloak/Entra ID needs no rebuild.
  const cfg: AppConfig = await (await fetch('/config.json')).json();

  const msal = new PublicClientApplication({
    auth: {
      clientId: cfg.clientId,
      authority: cfg.authority,
      knownAuthorities: cfg.knownAuthorities,
      redirectUri: cfg.redirectUri,
    },
    // msal-browser 5.x moved protocolMode from auth to system.
    system: { protocolMode: cfg.protocolMode === 'OIDC' ? ProtocolMode.OIDC : ProtocolMode.AAD },
    cache: { cacheLocation: 'sessionStorage' },
  });
  await msal.initialize();
  await msal.handleRedirectPromise();

  await bootstrapApplication(App, {
    providers: [
      ...appConfig.providers,
      { provide: APP_CONFIG, useValue: cfg },
      { provide: MSAL_INSTANCE, useValue: msal },
    ],
  });
}

start().catch((err) => console.error(err));
