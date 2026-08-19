// Dev-server proxy.
//
// Everything the browser sends to /api, /bff and the OIDC callbacks goes to the BFF, so the browser only ever
// talks to one origin — which is what makes the HttpOnly, SameSite=Strict session cookie behave in development
// exactly as it will in production, where the BFF serves the built bundle itself.
//
// The Aspire AppHost passes the BFF's address as CRACRA_BFF_URL. The fallback is for running `ng serve` on its own.
const target = process.env['CRACRA_BFF_URL'] ?? 'http://localhost:5000';

const forwardToBff = {
  target,
  secure: false,
  changeOrigin: false,
  cookieDomainRewrite: 'localhost',
};

export default {
  '/api': forwardToBff,
  '/bff': forwardToBff,
  '/signin-oidc': forwardToBff,
  '/signout-callback-oidc': forwardToBff,
};
