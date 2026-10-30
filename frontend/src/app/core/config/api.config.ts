// Local development runs Angular and the API on separate ports. A deployed build
// uses the same-origin /api path, allowing the reverse proxy to route API traffic.
export const API_BASE_URL =
  typeof window !== 'undefined' && window.location.hostname === 'localhost'
    ? 'http://localhost:5144/api'
    : '/api';
