import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideAnimations } from '@angular/platform-browser/animations';

import { inventoryCodeInterceptor } from './inventory-code.interceptor';
import { accessInterceptor } from './access.service';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withInterceptors([accessInterceptor, inventoryCodeInterceptor])),
    provideAnimations(),
    provideRouter(routes)
  ]
};
