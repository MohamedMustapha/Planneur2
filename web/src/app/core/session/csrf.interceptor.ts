import { HttpInterceptorFn } from '@angular/common/http';

/** Must match `BffEndpoints.AntiForgeryHeader` on the server. */
export const ANTI_FORGERY_HEADER = 'X-Cracra-Csrf';

/**
 * Adds the anti-forgery header to every same-origin API call.
 *
 * The session is a cookie, which a cross-site request would send automatically. It could not, however, add this
 * header — so requiring it is what makes the cookie safe to use. The BFF rejects any proxied `/api` request
 * without it.
 */
export const csrfInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api')) {
    return next(request);
  }

  return next(request.clone({ setHeaders: { [ANTI_FORGERY_HEADER]: '1' } }));
};
