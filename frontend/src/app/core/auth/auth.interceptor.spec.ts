import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import { API_BASE_URL } from '../config/api.config';

describe('authInterceptor', () => {
  let client: HttpClient;
  let http: HttpTestingController;
  let auth: AuthService;

  const session = {
    userId: 'user-id',
    email: 'person@example.com',
    fullName: 'Test Person',
    roles: ['Engineer'],
    accessToken: 'access-token',
    accessTokenExpiresAtUtc: '2026-10-27T01:00:00Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    client = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    auth.login({ email: session.email, password: 'correct horse battery' }).subscribe();
    http.expectOne(`${API_BASE_URL}/auth/login`).flush(session);
  });

  afterEach(() => http.verify());

  it('refreshes once and retries a request after an expired access token', () => {
    client.get(`${API_BASE_URL}/projects`).subscribe();

    const firstRequest = http.expectOne(`${API_BASE_URL}/projects`);
    expect(firstRequest.request.headers.get('Authorization')).toBe('Bearer access-token');
    firstRequest.flush({}, { status: 401, statusText: 'Unauthorized' });

    const refreshRequest = http.expectOne(`${API_BASE_URL}/auth/refresh`);
    expect(refreshRequest.request.withCredentials).toBe(true);
    refreshRequest.flush({ ...session, accessToken: 'new-access-token' });

    const retriedRequest = http.expectOne(`${API_BASE_URL}/projects`);
    expect(retriedRequest.request.headers.get('Authorization')).toBe('Bearer new-access-token');
    retriedRequest.flush({ ok: true });
  });

  it('does not send bearer tokens or credentials to an external or lookalike URL', () => {
    for (const url of ['https://external.example/data', `${API_BASE_URL}-other/projects`]) {
      client.get(url).subscribe();
      const request = http.expectOne(url);
      expect(request.request.headers.has('Authorization')).toBe(false);
      expect(request.request.withCredentials).toBe(false);
      request.flush({});
    }
  });

  it('keeps a refreshed session when the retried resource is forbidden', () => {
    let status: number | undefined;
    client.get(`${API_BASE_URL}/projects`).subscribe({ error: error => status = error.status });
    http.expectOne(`${API_BASE_URL}/projects`).flush({}, { status: 401, statusText: 'Unauthorized' });
    http.expectOne(`${API_BASE_URL}/auth/refresh`).flush({ ...session, accessToken: 'new-access-token' });
    http.expectOne(`${API_BASE_URL}/projects`).flush({}, { status: 403, statusText: 'Forbidden' });
    expect(status).toBe(403);
    expect(auth.accessToken).toBe('new-access-token');
  });
});
