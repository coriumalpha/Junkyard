import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { distinctUntilChanged, filter, map, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatSidenavModule } from '@angular/material/sidenav';

import { legacyUrl } from './legacy-url';

@Component({
  selector: 'app-root',
  imports: [
    MatButtonModule,
    MatDividerModule,
    MatIconModule,
    MatListModule,
    MatSidenavModule,
    RouterLink,
    RouterLinkActive,
    RouterOutlet
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly drawerCollapsed = signal(false);
  protected readonly drawerOpen = signal(true);
  protected readonly handset = signal(false);
  protected readonly currentUrl = signal('');
  protected readonly legacyHomeUrl = legacyUrl('/');

  private readonly destroyRef = inject(DestroyRef);
  private readonly breakpointObserver = inject(BreakpointObserver);
  private readonly router = inject(Router);

  constructor() {
    this.currentUrl.set(this.router.url);

    this.breakpointObserver.observe(['(max-width: 1024px)']).pipe(
      map((result) => result.matches),
      distinctUntilChanged(),
      tap((matches) => {
        this.handset.set(matches);
        this.drawerOpen.set(!matches);
        if (matches) {
          this.drawerCollapsed.set(false);
        }
      }),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();

    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      tap((event) => this.currentUrl.set(event.urlAfterRedirects)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected toggleDrawer(): void {
    if (this.handset()) {
      this.drawerOpen.update((value) => !value);
      return;
    }

    this.drawerCollapsed.update((value) => !value);
  }

  protected closeDrawer(): void {
    if (this.handset()) {
      this.drawerOpen.set(false);
    }
  }

  protected isInventorySection(): boolean {
    const path = this.currentUrl().split('?')[0];
    if (path.startsWith('/item/') || path.startsWith('/boxes/')) {
      return true;
    }

    return path.startsWith('/inventory') && !this.hasQueryFlag('onlyConsumable') && !this.hasQueryFlag('onlyOrphans');
  }

  protected isConsumablesSection(): boolean {
    return this.currentUrl().split('?')[0].startsWith('/inventory') && this.hasQueryFlag('onlyConsumable');
  }

  protected isOrphansSection(): boolean {
    return this.currentUrl().split('?')[0].startsWith('/inventory') && this.hasQueryFlag('onlyOrphans');
  }

  protected isSettingsSection(): boolean {
    return this.currentUrl().split('?')[0].startsWith('/settings');
  }

  private hasQueryFlag(name: string): boolean {
    const query = this.currentUrl().split('?')[1] ?? '';
    return new URLSearchParams(query).get(name)?.toLowerCase() === 'true';
  }
}
