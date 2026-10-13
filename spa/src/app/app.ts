import { Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { JsonPipe } from '@angular/common';
import { MsalService } from '@azure/msal-angular';
import { AccountInfo, InteractionRequiredAuthError } from '@azure/msal-browser';
import { APP_CONFIG } from './app-config';

@Component({
  imports: [JsonPipe],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  private readonly msal = inject(MsalService);
  private readonly http = inject(HttpClient);
  private readonly cfg = inject(APP_CONFIG);

  protected readonly account = signal<AccountInfo | null>(this.initialAccount());
  protected readonly result = signal<unknown>(null);
  protected readonly error = signal<string | null>(null);

  login() {
    this.msal.instance.loginRedirect({ scopes: this.cfg.scopes });
  }

  logout() {
    this.msal.instance.logoutRedirect({ account: this.account() ?? undefined });
  }

  async callApi() {
    this.error.set(null);
    this.result.set(null);
    try {
      const token = await this.getToken();
      this.http
        .get(`${this.cfg.apiBaseUrl}/me`, { headers: { Authorization: `Bearer ${token}` } })
        .subscribe({
          next: (r) => this.result.set(r),
          error: (e) => this.error.set(`API returned ${e.status}`),
        });
    } catch (e) {
      this.error.set(String(e));
    }
  }

  private async getToken(): Promise<string> {
    const request = { scopes: this.cfg.scopes, account: this.account()! };
    try {
      return (await this.msal.instance.acquireTokenSilent(request)).accessToken;
    } catch (e) {
      if (e instanceof InteractionRequiredAuthError) {
        await this.msal.instance.acquireTokenRedirect(request);
      }
      throw e;
    }
  }

  private initialAccount(): AccountInfo | null {
    const accounts = this.msal.instance.getAllAccounts();
    if (accounts.length) this.msal.instance.setActiveAccount(accounts[0]);
    return accounts[0] ?? null;
  }
}
