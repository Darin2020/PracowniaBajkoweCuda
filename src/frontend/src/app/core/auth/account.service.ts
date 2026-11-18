import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Account, AccountLogins } from './auth.models';
import { AuthService } from './auth.service';

/** `/api/account` (docs/PLAN.md §3.2, §6): the account page's calls. */
@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  /** "Export my data" is a plain download link: the browser sends the session cookie with it. */
  readonly exportUrl = '/api/account/export';

  get(): Promise<Account> {
    return firstValueFrom(this.http.get<Account>('/api/account'));
  }

  async update(changes: { displayName?: string; preferredLanguage?: string }): Promise<Account> {
    const account = await firstValueFrom(this.http.patch<Account>('/api/account', changes));
    await this.auth.refresh();
    return account;
  }

  logins(): Promise<AccountLogins> {
    return firstValueFrom(this.http.get<AccountLogins>('/api/account/logins'));
  }

  async removeLogin(provider: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/account/logins/${encodeURIComponent(provider)}`));
  }

  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    await firstValueFrom(this.http.post('/api/account/password', { currentPassword, newPassword }));
  }

  /** Needs the password, or (for an account without one) an external re-authentication just before. */
  async delete(password: string | null): Promise<void> {
    await firstValueFrom(this.http.post('/api/account/delete', { password }));
    this.auth.clear();
  }
}
