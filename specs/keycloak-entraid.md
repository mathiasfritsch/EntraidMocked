## Handoff: Local Keycloak / Entra ID switch by configuration

**Goal:** Run Keycloak in Docker as a local replacement for Microsoft Entra ID, while keeping the existing Angular and .NET applications and their MSAL integration. The application must be able to switch between **Keycloak** and **Entra ID exclusively through configuration**—no code changes or rebuilds when changing provider.

### Scope

- Local development and testing use **Keycloak** in Docker.
- Production or Entra-connected environments use **Microsoft Entra ID**.
- The same Angular MSAL and .NET authentication code paths are used for both providers.
- Only issuer/authority, client ID, protocol mode, scopes, redirect URIs, and claim mappings differ between environments.

Keycloak supports the required OIDC/OAuth2 flows, including authorization code + PKCE, token issuance, refresh tokens, logout, and JWT-based API validation. [learn.microsoft](https://learn.microsoft.com/en-us/entra/msal/javascript/browser/configuration)

### Architecture

```text
                 ┌────────────────────────┐
                 │ Configuration / env     │
                 │ Keycloak OR Entra ID    │
                 └───────────┬────────────┘
                             v
Angular app (MSAL) ──────> Selected Identity Provider
        │
        │ JWT access token
        v
.NET API ────────────────> Validates token from selected issuer
```

The identity provider is selected through environment-specific configuration. The application does not contain provider-specific branching.

### Docker setup

Use Docker Compose with:

- `keycloak` exposed on `127.0.0.1:8080`
- PostgreSQL with a persistent volume for realms, clients, users, and roles
- Keycloak in `start-dev` mode for local development only

```yaml
services:
  keycloak:
    image: quay.io/keycloak/keycloak:26.8.0
    command: start-dev
    environment:
      KC_BOOTSTRAP_ADMIN_USERNAME: admin
      KC_BOOTSTRAP_ADMIN_PASSWORD: admin
      KC_DB: postgres
      KC_DB_URL: jdbc:postgresql://keycloak-db:5432/keycloak
      KC_DB_USERNAME: keycloak
      KC_DB_PASSWORD: keycloak
    ports:
      - "127.0.0.1:8080:8080"
    depends_on:
      keycloak-db:
        condition: service_healthy

  keycloak-db:
    image: postgres:16
    environment:
      POSTGRES_DB: keycloak
      POSTGRES_USER: keycloak
      POSTGRES_PASSWORD: keycloak
    volumes:
      - keycloak-db-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U keycloak"]
      interval: 5s
      timeout: 5s
      retries: 10

volumes:
  keycloak-db-data:
```

`start-dev` is intended for local development only and must not be exposed beyond localhost or used in production. 

### Provider configuration

All provider-specific values must come from configuration, for example `appsettings.json`, environment variables, or Angular environment files.

| Configuration item | Local Keycloak | Entra ID |
|---|---|---|
| Identity provider | `Keycloak` | `EntraId` |
| Authority / issuer | `http://localhost:8080/realms/local` | `https://login.microsoftonline.com/<tenant-id>/v2.0` |
| Client ID | Keycloak client ID | Entra app registration client ID |
| Protocol mode | `OIDC` | Microsoft/Entra default |
| Known authority | `localhost:8080` | `login.microsoftonline.com` |
| Redirect URI | `http://localhost:4200` | Production/app-registration redirect URI |
| API scope | Local Keycloak scope | Entra API scope |
| Token validation issuer | Keycloak realm issuer | Entra tenant issuer |
| Role/group claims | Keycloak-mapped claims | Entra roles/groups claims |

### Angular / MSAL configuration

Keep MSAL, but make these values configurable:

```ts
authority: config.identity.authority,
clientId: config.identity.clientId,
knownAuthorities: config.identity.knownAuthorities,
protocolMode: config.identity.protocolMode,
redirectUri: config.identity.redirectUri,
```

For Keycloak:

```ts
{
  authority: 'http://localhost:8080/realms/local',
  clientId: 'frontend',
  knownAuthorities: ['localhost:8080'],
  protocolMode: ProtocolMode.OIDC,
  redirectUri: 'http://localhost:4200'
}
```

For Entra ID:

```ts
{
  authority: 'https://login.microsoftonline.com/<tenant-id>/v2.0',
  clientId: '<entra-client-id>',
  knownAuthorities: ['login.microsoftonline.com'],
  protocolMode: ProtocolMode.AAD,
  redirectUri: 'https://app.example.com'
}
```

MSAL must use generic OIDC mode for Keycloak; its default behavior assumes Microsoft Entra endpoints. [learn.microsoft](https://learn.microsoft.com/en-us/entra/msal/javascript/browser/configuration)

### .NET API configuration

The .NET API validates JWTs from whichever issuer is configured:

```json
{
  "Identity": {
    "Provider": "Keycloak",
    "Authority": "http://localhost:8080/realms/local",
    "Audience": "api",
    "ValidIssuers": [
      "http://localhost:8080/realms/local"
    ]
  }
}
```

For Entra ID:

```json
{
  "Identity": {
    "Provider": "EntraId",
    "Authority": "https://login.microsoftonline.com/<tenant-id>/v2.0",
    "Audience": "<api-client-id>",
    "ValidIssuers": [
      "https://sts.windows.net/<tenant-id>/",
      "https://login.microsoftonline.com/<tenant-id>/v2.0"
    ]
  }
}
```

The API should use standard JWT bearer validation and must not hard-code either provider. [skycloak](https://skycloak.io/blog/keycloak-dotnet-aspnet-core-authentication/)

### Required implementation rule

**No authentication or authorization logic may be provider-specific.**

The application must:

- Read provider settings from configuration.
- Use the configured authority, client ID, scopes, and redirect URI.
- Validate tokens based on the configured issuer/audience.
- Map authorization roles or permissions from claims in a provider-neutral way.
- Avoid dependencies on Entra-only claims, Microsoft Graph tokens, or Azure-specific authentication features. 

### Keycloak realm setup

Create a `local` realm with:

- Public Angular client `frontend`
- Confidential .NET API client `api`, if client-credentials or machine-to-machine calls are needed
- Test users with roles/groups
- Claims mapped into access tokens so the .NET API can authorize consistently
- Short access-token, refresh-token, and SSO session lifetimes for testing expiry and forced re-login

Keycloak can reproduce the important refresh-token sequence: access-token expiry, successful refresh, refresh-token expiry or revocation, `invalid_grant`, session cleanup, and interactive sign-in. 

### Acceptance criteria

1. Starting the Docker stack enables Keycloak as the configured identity provider.
2. Angular signs in through MSAL using Keycloak.
3. The .NET API accepts Keycloak-issued access tokens.
4. Access-token refresh works through MSAL.
5. Expired or revoked refresh tokens cause `invalid_grant`, followed by forced interactive login.
6. Changing only configuration switches the application to Entra ID.
7. No source-code changes, branching, or rebuild is required to change provider.
8. Authorization continues to work through provider-neutral role/claim mapping.

### Out of scope

- Microsoft Graph authentication
- Azure managed identities
- Entra Conditional Access, MFA, or tenant policy simulation
- Exact reproduction of Entra token claims or lifetimes
- Production-grade Keycloak deployment; Keycloak local mode is for development only.