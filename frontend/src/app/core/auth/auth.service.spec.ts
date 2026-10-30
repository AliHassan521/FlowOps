import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';
import { API_BASE_URL } from '../config/api.config';

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  const response = {
    userId: 'user-id',
    email: 'person@example.com',
    fullName: 'Test Person',
    roles: ['Engineer'],
    accessToken: 'access-token',
    accessTokenExpiresAtUtc: '2026-10-27T01:00:00Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('stores the access token after login and sends credentials', () => {
    service.login({ email: 'person@example.com', password: 'correct horse battery' }).subscribe();

    const request = http.expectOne(`${API_BASE_URL}/auth/login`);
    expect(request.request.withCredentials).toBe(true);
    request.flush(response);

    expect(service.accessToken).toBe('access-token');
    expect(service.isAuthenticated).toBe(true);
  });

  it('shares concurrent refresh requests', () => {
    service.refresh().subscribe();
    service.refresh().subscribe();

    const requests = http.match(`${API_BASE_URL}/auth/refresh`);
    expect(requests.length).toBe(1);
    requests[0].flush(response);
    expect(service.isAuthenticated).toBe(true);
  });

  it('clears the session when logout completes', () => {
    service.login({ email: 'person@example.com', password: 'correct horse battery' }).subscribe();
    http.expectOne(`${API_BASE_URL}/auth/login`).flush(response);

    service.logout().subscribe();
    http.expectOne(`${API_BASE_URL}/auth/logout`).flush(null);

    expect(service.isAuthenticated).toBe(false);
  });
});
