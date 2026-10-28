import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { AccessService } from './access.service';
@Component({
 selector:'app-access-login',standalone:true,imports:[FormsModule,MatButtonModule,MatFormFieldModule,MatInputModule,MatCheckboxModule],
 template:`<main class="access-screen"><form (ngSubmit)="login()">
 <p class="eyebrow">Junkyard · Inventario privado</p><h1>Tu inventario,<br>solo para ti.</h1>
 @if(!access.ready()){<p>Comprobando acceso…</p>}
 @else if(!access.configured()){<p>Falta crear la cuenta inicial. El administrador debe completar la configuración local; no se han modificado los datos del inventario.</p>}
 @else {
 <p>Inicia sesión para consultar y editar tus objetos.</p>
 <mat-form-field appearance="outline"><mat-label>Usuario</mat-label><input matInput name="username" [(ngModel)]="username" autocomplete="username" maxlength="100" required></mat-form-field>
 <mat-form-field appearance="outline"><mat-label>Contraseña</mat-label><input matInput type="password" name="password" [(ngModel)]="password" autocomplete="current-password" maxlength="256" required></mat-form-field>
 <mat-checkbox name="remember" [(ngModel)]="remember">Recordar sesión durante 30 días</mat-checkbox>
 <p class="session-hint">Solo en tus dispositivos. Puedes cerrarla desde el menú de usuario.</p>
 <button mat-flat-button type="submit" [disabled]="busy()">{{busy()?'Entrando…':'Entrar'}}</button>
 }
 @if(error() || access.error()){<p role="alert">{{error() || access.error()}}</p>}
 <button mat-button type="button" (click)="access.refresh()" [disabled]="busy()">Comprobar conexión</button>
 </form></main>`,
 styles:[`.access-screen{min-height:100dvh;display:grid;place-items:center;padding:24px;background:var(--app-bg)}form{width:min(440px,100%);padding:32px;background:var(--app-surface);border:1px solid var(--app-border);border-top:3px solid var(--app-accent);border-radius:8px}h1{font-size:34px;line-height:1.15;margin:20px 0}p{color:var(--app-muted);line-height:1.6}mat-form-field{width:100%;margin-top:12px}button[type=submit]{width:100%;min-height:44px}[role=alert]{color:var(--app-warning)}.eyebrow{font-size:13px}.session-hint{font-size:12px;margin:4px 0 20px;line-height:1.4}fieldset{border:0;padding:0;margin-top:24px;min-width:0}legend{font-weight:600;font-size:14px;color:var(--app-accent)}`]
})
export class AccessLoginComponent {
 readonly access=inject(AccessService);readonly busy=signal(false);readonly error=signal('');username='operador';password='';remember=false;
 async login(){if(this.busy())return;this.busy.set(true);this.error.set('');
  try{
    await this.access.login(this.username,this.password,this.remember);
    this.password='';
  }
  catch(e:any){this.error.set(e.status===429?'Demasiados intentos. Espera un minuto antes de reintentar.':e.error?.error || 'No se pudo iniciar sesión.');}
  finally{this.busy.set(false);}
 }
}
