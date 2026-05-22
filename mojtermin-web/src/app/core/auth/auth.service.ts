import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, finalize, shareReplay, tap, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AuthResponse,
  RegisterBusinessRequest,
  RegisterBusinessResponse
} from '../../shared/models/business.models';

const TOKEN_KEY = 'mojtermin_access_token';
const REFRESH_TOKEN_KEY = 'mojtermin_refresh_token';

@Injectable({ providedIn: 'root' })
export class AuthService {
  readonly token = signal<string | null>(localStorage.getItem(TOKEN_KEY));
  readonly isAuthenticated = signal<boolean>(!!localStorage.getItem(TOKEN_KEY));
  private refreshInFlight$: Observable<AuthResponse> | null = null;

  constructor(private readonly http: HttpClient) {}

  login(usernameOrEmail: string, password: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${environment.apiBaseUrl}/auth/login`, { usernameOrEmail, password })
      .pipe(tap((response) => this.storeTokens(response.token, response.refreshToken)));
  }

  /**
   * Registers a new business+owner. With strict email verification turned on,
   * this no longer auto-logs the user in; the returned payload carries the
   * pending-verification flag and the SPA must route to /verification-pending.
   */
  registerBusiness(payload: RegisterBusinessRequest): Observable<RegisterBusinessResponse> {
    return this.http.post<RegisterBusinessResponse>(
      `${environment.apiBaseUrl}/businesses/register`,
      payload
    );
  }

  /**
   * Exchanges the raw verification token from the email link for a real auth
   * session. On success the API returns the same shape as /login, so we hook
   * into storeTokens and the user is dropped straight into the admin panel.
   */
  verifyEmail(token: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${environment.apiBaseUrl}/auth/verify-email`, { token })
      .pipe(tap((response) => this.storeTokens(response.token, response.refreshToken)));
  }

  /**
   * Asks the API to re-send the verification email. The endpoint deliberately
   * returns the same message whether the email exists or not (anti-enumeration),
   * so this method just resolves with a generic ack object.
   */
  resendVerification(email: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(
      `${environment.apiBaseUrl}/auth/resend-verification`,
      { email }
    );
  }

  /**
   * Triggers a password-reset email. The backend always returns the same
   * generic message regardless of whether the email is known, so the SPA
   * can show a consistent "check your inbox" screen without leaking which
   * addresses are registered.
   */
  forgotPassword(email: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(
      `${environment.apiBaseUrl}/auth/forgot-password`,
      { email }
    );
  }

  /**
   * Completes the reset flow. On success the API returns a full AuthResponse
   * and we auto-login the user, dropping them straight into the admin panel.
   */
  resetPassword(token: string, newPassword: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${environment.apiBaseUrl}/auth/reset-password`, {
        token,
        newPassword
      })
      .pipe(tap((response) => this.storeTokens(response.token, response.refreshToken)));
  }

  /**
   * Authenticated change-password from the admin panel. Returns a simple ack;
   * the API also revokes all outstanding refresh tokens for this user, so the
   * SPA should expect future /refresh calls to fail with 401.
   */
  changePassword(currentPassword: string, newPassword: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(
      `${environment.apiBaseUrl}/auth/change-password`,
      { currentPassword, newPassword }
    );
  }

  refresh(): Observable<AuthResponse> {
    const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);
    if (!refreshToken) {
      return throwError(() => new Error('Nedostaje refresh token.'));
    }

    if (this.refreshInFlight$) {
      return this.refreshInFlight$;
    }

    this.refreshInFlight$ = this.http
      .post<AuthResponse>(`${environment.apiBaseUrl}/auth/refresh`, { refreshToken })
      .pipe(
        tap((response) => this.storeTokens(response.token, response.refreshToken)),
        finalize(() => {
          this.refreshInFlight$ = null;
        }),
        shareReplay(1)
      );

    return this.refreshInFlight$;
  }

  logout(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
    this.token.set(null);
    this.isAuthenticated.set(false);
  }

  getAccessToken(): string | null {
    return localStorage.getItem(TOKEN_KEY);
  }

  hasRefreshToken(): boolean {
    return !!localStorage.getItem(REFRESH_TOKEN_KEY);
  }

  private storeTokens(accessToken: string, refreshToken: string): void {
    localStorage.setItem(TOKEN_KEY, accessToken);
    localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
    this.token.set(accessToken);
    this.isAuthenticated.set(true);
  }
}
