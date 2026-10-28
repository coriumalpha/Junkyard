import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpInterceptorFn } from '@angular/common/http';
import { catchError, firstValueFrom, throwError } from 'rxjs';

interface AccessSession {enabled:boolean; authenticated:boolean; username:string|null; configured:boolean;  csrfToken:string;}
@Injectable({providedIn:'root'})
export class AccessService {
  private http=inject(HttpClient);
  readonly ready=signal(false);
  readonly allowed=signal(false);
  readonly everAllowed=signal(false);
  readonly configured=signal(true);
  readonly username=signal<string|null>(null);
  readonly error=signal('');
  readonly csrf=signal('');
  async refresh():Promise<void> {
    try {
      const session=await firstValueFrom(this.http.get<AccessSession>('/api/auth/session'));
      this.csrf.set(session.csrfToken);this.configured.set(session.configured);
      this.allowed.set(!session.enabled || session.authenticated);
      this.username.set(session.username);
      if(this.allowed())this.everAllowed.set(true);
      this.error.set('');
    } catch {
      this.allowed.set(false);this.error.set('No se pudo comprobar el acceso. Reintenta desde la dirección HTTPS del inventario.');
    } finally {this.ready.set(true);}
  }
  async login(username:string,password:string,remember=false):Promise<void> {
    await firstValueFrom(this.http.post('/api/auth/login',{username,password,remember}));
    await this.refresh();
  }
  async logout():Promise<void> {
    await firstValueFrom(this.http.post('/api/auth/logout',{}));
    // Explicit logout clears all loaded inventory data, unlike session expiry.
    location.reload();
  }
}
export const accessInterceptor:HttpInterceptorFn=(request,next)=>{
  const access=inject(AccessService);
  const write=!['GET','HEAD','OPTIONS'].includes(request.method);
  const local=request.url.startsWith('/api/');
  if(local && write && access.csrf()) request=request.clone({setHeaders:{'X-Junkyard-CSRF':access.csrf()}});
  return next(request).pipe(catchError(error=>{
    if(error.status===401 && local && !request.url.startsWith('/api/auth/')) {
      access.allowed.set(false);
      access.error.set('Tu sesión ha caducado. Vuelve a entrar; los cambios abiertos siguen en esta pestaña.');
      // Obtain an anonymous CSRF token for the reauthentication form.
      void access.refresh();
    }
    return throwError(()=>error);
  }));
};
