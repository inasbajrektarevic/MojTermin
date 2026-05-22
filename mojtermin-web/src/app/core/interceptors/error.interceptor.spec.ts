import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { ToastService } from '../services/toast.service';
import { authInterceptor } from './auth.interceptor';
import { errorInterceptor } from './error.interceptor';
import { environment } from '../../../environments/environment';

describe('errorInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let authService: AuthService;
  let toastService: jasmine.SpyObj<ToastService>;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    toastService = jasmine.createSpyObj<ToastService>('ToastService', ['error', 'success', 'info']);

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toastService }
      ]
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    authService = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl').and.resolveTo(true);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('refreshes token and retries original request on 401', () => {
    localStorage.setItem('mojtermin_access_token', 'old-access');
    localStorage.setItem('mojtermin_refresh_token', 'refresh-old');

    let responseBody: { ok: boolean } | null = null;
    http.get<{ ok: boolean }>(`${environment.apiBaseUrl}/dashboard/summary`).subscribe((result) => {
      responseBody = result;
    });

    const firstAttempt = httpMock.expectOne(`${environment.apiBaseUrl}/dashboard/summary`);
    expect(firstAttempt.request.headers.get('Authorization')).toBe('Bearer old-access');
    firstAttempt.flush({}, { status: 401, statusText: 'Unauthorized' });

    const refresh = httpMock.expectOne(`${environment.apiBaseUrl}/auth/refresh`);
    expect(refresh.request.method).toBe('POST');
    refresh.flush({
      token: 'new-access',
      refreshToken: 'refresh-new',
      expiresAtUtc: new Date(Date.now() + 3600_000).toISOString(),
      userId: 'user-id',
      businessId: 'business-id',
      fullName: 'Owner User',
      username: 'owner',
      role: 'Owner'
    });

    const retried = httpMock.expectOne(`${environment.apiBaseUrl}/dashboard/summary`);
    expect(retried.request.headers.get('Authorization')).toBe('Bearer new-access');
    retried.flush({ ok: true });

    expect(responseBody as unknown).toEqual({ ok: true });
    expect(toastService.error).not.toHaveBeenCalled();
  });

  it('logs out and redirects when refresh fails', () => {
    localStorage.setItem('mojtermin_access_token', 'old-access');
    localStorage.setItem('mojtermin_refresh_token', 'refresh-old');

    http.get(`${environment.apiBaseUrl}/services`).subscribe({
      next: () => fail('Expected request to fail'),
      error: () => {
        // expected
      }
    });

    httpMock.expectOne(`${environment.apiBaseUrl}/services`).flush({}, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectOne(`${environment.apiBaseUrl}/auth/refresh`).flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(authService.getAccessToken()).toBeNull();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/admin/login');
    expect(toastService.error).toHaveBeenCalledWith('Sesija je istekla. Prijavite se ponovo.');
  });
});
