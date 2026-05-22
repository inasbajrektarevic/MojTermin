import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';
import { environment } from '../../../environments/environment';
import { AuthResponse } from '../../shared/models/business.models';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  const authResponse: AuthResponse = {
    token: 'access-token-new',
    refreshToken: 'refresh-token-new',
    expiresAtUtc: new Date(Date.now() + 3600_000).toISOString(),
    userId: 'user-id',
    businessId: 'business-id',
    fullName: 'Owner User',
    username: 'owner',
    role: 'Owner'
  };

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('stores tokens on login', () => {
    service.login('owner', 'Owner123!').subscribe((response) => {
      expect(response.token).toBe(authResponse.token);
      expect(service.getAccessToken()).toBe(authResponse.token);
      expect(service.isAuthenticated()).toBeTrue();
    });

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/auth/login`);
    expect(req.request.method).toBe('POST');
    req.flush(authResponse);
  });

  it('reuses single refresh request for parallel calls', () => {
    localStorage.setItem('mojtermin_refresh_token', 'refresh-token-old');

    let firstToken = '';
    let secondToken = '';

    service.refresh().subscribe((response) => {
      firstToken = response.token;
    });
    service.refresh().subscribe((response) => {
      secondToken = response.token;
    });

    const refreshReq = httpMock.expectOne(`${environment.apiBaseUrl}/auth/refresh`);
    expect(refreshReq.request.method).toBe('POST');
    refreshReq.flush(authResponse);

    expect(firstToken).toBe('access-token-new');
    expect(secondToken).toBe('access-token-new');
    expect(service.getAccessToken()).toBe('access-token-new');
  });
});
