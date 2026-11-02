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

  it('registers an account with credentials and stores the returned session', () => {
    service.register({ fullName: 'Test Person', email: 'person@example.com', password: 'correct horse battery' }).subscribe();

    const request = http.expectOne(`${API_BASE_URL}/auth/register`);
    expect(request.request.method).toBe('POST');
    expect(request.request.withCredentials).toBe(true);
    request.flush(response);

    expect(service.currentUser()?.userId).toBe('user-id');
    expect(service.accessToken).toBe('access-token');
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

  it('serializes logout after an in-flight refresh and never resurrects the local session', () => {
    service.login({ email: response.email, password: 'correct horse battery' }).subscribe();
    http.expectOne(`${API_BASE_URL}/auth/login`).flush(response);
    service.refresh().subscribe({ error: () => {} });
    const refresh = http.expectOne(`${API_BASE_URL}/auth/refresh`);
    service.logout().subscribe();
    expect(service.isAuthenticated).toBe(false);
    http.expectNone(`${API_BASE_URL}/auth/logout`);
    refresh.flush({ ...response, accessToken: 'late-refresh-token' });
    expect(service.isAuthenticated).toBe(false);
    http.expectOne(`${API_BASE_URL}/auth/logout`).flush(null);
    expect(service.isAuthenticated).toBe(false);
  });

  it('orders a new login after a stale refresh and accepts only the new identity', () => {
    service.refresh().subscribe({ error: () => {} });
    const refresh = http.expectOne(`${API_BASE_URL}/auth/refresh`);
    service.login({ email: 'other@example.test', password: 'correct horse battery' }).subscribe();
    http.expectNone(`${API_BASE_URL}/auth/login`);
    refresh.flush(response);
    expect(service.isAuthenticated).toBe(false);
    http.expectOne(`${API_BASE_URL}/auth/login`).flush({ ...response, userId: 'other-user' });
    expect(service.currentUser()?.userId).toBe('other-user');
  });

  it('finishes a cookie request after unsubscription and orders logout after a failed refresh', () => {
    const subscription = service.refresh().subscribe({ error: () => {} });
    const refresh = http.expectOne(`${API_BASE_URL}/auth/refresh`);
    subscription.unsubscribe();
    expect(refresh.cancelled).toBe(false);
    service.logout().subscribe();
    http.expectNone(`${API_BASE_URL}/auth/logout`);
    refresh.flush({}, { status: 503, statusText: 'Unavailable' });
    http.expectOne(`${API_BASE_URL}/auth/logout`).flush(null);
    expect(service.isAuthenticated).toBe(false);
  });

  it('an older logout response cannot clear a subsequently requested login', () => {
    service.logout().subscribe();
    const logout = http.expectOne(`${API_BASE_URL}/auth/logout`);
    service.login({ email: response.email, password: 'correct horse battery' }).subscribe();
    http.expectNone(`${API_BASE_URL}/auth/login`);
    logout.flush(null);
    http.expectOne(`${API_BASE_URL}/auth/login`).flush(response);
    expect(service.accessToken).toBe(response.accessToken);
  });
});
