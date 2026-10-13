# EntraidMocked

Local playground for running an MSAL-based Angular SPA and a .NET API against a **local Keycloak** instead of Microsoft Entra ID.

## Why this repo exists

Testing MSAL behavior against a real Entra ID tenant is slow and hard to control. This is especially true for the refresh-token sequence: access-token expiry, silent refresh, refresh-token expiry or revocation, `invalid_grant`, and the forced interactive re-login. See [problem-statement/entraid-refersh-token.svg](problem-statement/entraid-refersh-token.svg).

Keycloak runs locally in Docker, supports the same OIDC flows (authorization code + PKCE, refresh tokens, logout, JWT access tokens), and lets us shorten token and session lifetimes. The goal is to switch between Keycloak and Entra ID **by configuration only**, with no code change or rebuild. The design is in [specs/keycloak-entraid.md](specs/keycloak-entraid.md).

Not reproduced: Conditional Access, MFA, exact Entra claims or lifetimes, Microsoft Graph, managed identities.

## Layout

| Path | What |
|---|---|
| `docker-compose.yml` | Keycloak (HTTPS, port 8443) + Postgres |
| `keycloak/local-realm.json` | Realm `local`, clients `spa` and `api-docs`, user `alice` |
| `identity-client-web-app/` | .NET 10 API (`/me`), provider chosen by config |
| `spa/` | Angular + MSAL SPA that signs in and calls `/me` |

## Prerequisites

- Docker
- .NET 10 SDK
- Node.js and npm
- The ASP.NET dev certificate trusted: `dotnet dev-certs https --trust`

## Setup

1. Export the dev certificate for Keycloak. MSAL.js refuses non-`https` authorities, so Keycloak must serve TLS:

   ```powershell
   dotnet dev-certs https --export-path keycloak/certs/localhost.pem --format Pem --no-password
   ```

   `keycloak/certs/` is gitignored.

2. Start Keycloak (imports the realm on first start):

   ```powershell
   docker compose up -d
   ```

   Admin console: https://localhost:8443 (`admin` / `admin`). Realm `local` is at `https://localhost:8443/realms/local`.

3. Start the API with the Keycloak configuration:

   ```powershell
   cd identity-client-web-app
   $env:ASPNETCORE_ENVIRONMENT = 'Keycloak'
   dotnet run --no-launch-profile --urls https://localhost:5101
   ```

4. Start the SPA:

   ```powershell
   cd spa
   npm install
   npx ng serve --port 4200
   ```

## Usage

1. Open http://localhost:4200 and click **Sign in**.
2. Log in as `alice` / `alice`.
3. Click **Call /me**. The SPA acquires a token through MSAL and calls the API. You should see `name: alice`, `iss: https://localhost:8443/realms/local`, `aud: api`.

The API also serves Scalar at https://localhost:5101/scalar (client `api-docs`, PKCE).

### Changing the realm

The realm import only runs on an empty database. After editing `keycloak/local-realm.json`:

```powershell
docker compose down -v
docker compose up -d
```

Then clear session storage for `localhost:4200`, or the SPA shows a stale signed-in state.

## Configuration

Provider settings come from configuration, not code.

**API** (`identity-client-web-app`): `Identity:Provider` is `EntraId` (default, reads the `AzureAd` section) or `Keycloak` (reads `Identity`, plus `Cors:Origins`). The Keycloak values are in `appsettings.Keycloak.json` and load when `ASPNETCORE_ENVIRONMENT=Keycloak`.

**SPA**: `spa/public/config.json` is loaded at runtime (authority, client ID, known authorities, protocol mode, redirect URI, scopes, API base URL). For Entra ID use `protocolMode: "AAD"` and an Entra authority and client ID.

To use Entra ID, copy your real values into `appsettings.json` locally. Never commit the client secret.

## Notes

- msal-browser 5.x takes `protocolMode` under `system`, not `auth`.
- MSAL always requests `offline_access`, so the realm defines that scope.
- MSAL.NET has no generic OIDC mode, so it can't target Keycloak.
- The ports used are 8443 (Keycloak), 5101 (API) and 4200 (SPA). Port 5001 is only the default in the `launchSettings.json` profiles.
