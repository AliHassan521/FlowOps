// Local development runs Angular and the API on separate ports. A deployed build
// uses the same-origin /api path, allowing the reverse proxy to route API traffic.
export function apiBaseUrl(location?: Pick<Location, 'hostname' | 'port' | 'protocol'>): string {
  return location?.hostname === 'localhost' &&
    location.port === '4200' &&
    location.protocol === 'http:'
    ? 'http://localhost:5144/api'
    : '/api';
}

export const API_BASE_URL = apiBaseUrl(typeof window === 'undefined' ? undefined : window.location);
