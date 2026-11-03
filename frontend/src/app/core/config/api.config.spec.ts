import { apiBaseUrl } from './api.config';

describe('API origin selection', () => {
  it('uses the separate API only for the documented Angular development origin', () => {
    expect(apiBaseUrl({ hostname: 'localhost', port: '4200', protocol: 'http:' })).toBe(
      'http://localhost:5144/api',
    );
  });
  it.each([
    { hostname: 'localhost', port: '8080', protocol: 'http:' },
    { hostname: 'localhost', port: '', protocol: 'https:' },
    { hostname: 'localhost', port: '4200', protocol: 'https:' },
    { hostname: 'flowops.example', port: '', protocol: 'https:' },
    { hostname: '127.0.0.1', port: '8080', protocol: 'http:' },
    undefined,
  ])('keeps packaged and server-rendered builds on the same-origin API (%j)', (location) => {
    expect(apiBaseUrl(location)).toBe('/api');
  });
});
