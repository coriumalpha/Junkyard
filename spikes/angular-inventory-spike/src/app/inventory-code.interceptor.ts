import { HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { map } from 'rxjs';
import { normalizeInventoryCodes } from './inventory-code';
export const inventoryCodeInterceptor: HttpInterceptorFn = (request,next) => next(request).pipe(map(event =>
  request.url.startsWith('/api/') && request.responseType === 'json' && event instanceof HttpResponse
    ? event.clone({body:normalizeInventoryCodes(event.body)}) : event));
