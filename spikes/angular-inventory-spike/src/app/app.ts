import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, DestroyRef, ElementRef, HostListener, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { catchError, distinctUntilChanged, EMPTY, filter, finalize, map, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSidenavModule } from '@angular/material/sidenav';

import { InventoryApiService, InventoryGroup, InventoryItem } from './inventory-api.service';
import { InventoryCodePipe } from './inventory-code.pipe';
import { legacyUrl } from './legacy-url';

@Component({
  selector: 'app-root',
  imports: [
    FormsModule,
    MatButtonModule,
    MatDividerModule,
    MatIconModule,
    MatInputModule,
    MatListModule,
    MatProgressSpinnerModule,
    MatSidenavModule,
    InventoryCodePipe,
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
  protected readonly quickSearch = signal('');
  protected readonly quickItems = signal<InventoryItem[]>([]);
  protected readonly quickBoxes = signal<InventoryGroup[]>([]);
  protected readonly quickLoading = signal(false);
  protected readonly quickOpen = signal(false);
  protected readonly hasQuickResults = computed(() => Boolean(this.quickItems().length || this.quickBoxes().length));
  protected readonly legacyHomeUrl = legacyUrl('/');

  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly breakpointObserver = inject(BreakpointObserver);
  private readonly router = inject(Router);
  private readonly api = inject(InventoryApiService);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

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
      tap((event) => {
        this.currentUrl.set(event.urlAfterRedirects);
        this.resetContentScroll();
      }),
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

  protected updateQuickSearch(value: string): void {
    this.quickSearch.set(value);
    clearTimeout(this.searchTimer);
    const query = value.trim();
    if (query.length < 2) {
      this.quickItems.set([]);
      this.quickBoxes.set([]);
      this.quickLoading.set(false);
      this.quickOpen.set(false);
      return;
    }

    this.quickOpen.set(true);
    this.quickLoading.set(true);
    this.searchTimer = setTimeout(() => this.runQuickSearch(query), 180);
  }

  protected clearQuickSearch(): void {
    this.quickSearch.set('');
    this.quickItems.set([]);
    this.quickBoxes.set([]);
    this.quickLoading.set(false);
    this.quickOpen.set(false);
  }

  protected openQuickSearchPanel(): void {
    if (this.quickSearch().trim().length >= 2) {
      this.quickOpen.set(true);
    }
  }

  protected openQuickItem(item: InventoryItem): void {
    this.router.navigate(['/item', item.id]).then(() => {
      this.clearQuickSearch();
      this.closeDrawer();
    }).catch(() => undefined);
  }

  protected openQuickBox(box: InventoryGroup): void {
    if (!box.boxId) {
      return;
    }

    this.router.navigate(['/boxes', box.code]).then(() => {
      this.clearQuickSearch();
      this.closeDrawer();
    }).catch(() => undefined);
  }

  protected isInventorySection(): boolean {
    const path = this.currentUrl().split('?')[0];
    if (path.startsWith('/item/')) {
      return true;
    }

    return path.startsWith('/inventory') && !this.hasQueryFlag('onlyConsumable') && !this.hasQueryFlag('onlyOrphans');
  }

  protected isContainersSection(): boolean {
    const path = this.currentUrl().split('?')[0];
    return path.startsWith('/containers') || path.startsWith('/boxes/');
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

  protected tagSummary(item: InventoryItem): string {
    return item.tags.length ? item.tags.map((tag) => tag.name).join(', ') : item.category;
  }

  protected boxSummary(box: InventoryGroup): string {
    const parts = [`${box.itemCount} ítems`];
    if (box.childCount) {
      parts.push(`${box.childCount} hijos`);
    }
    if (box.locationName) {
      parts.push(box.locationName);
    }
    return parts.join(' · ');
  }

  protected assetUrl(path: string | null | undefined): string | null {
    return path ? (path.startsWith('/') ? path : `/${path}`) : null;
  }

  @HostListener('document:click', ['$event'])
  protected closeQuickSearchOnOutsideClick(event: MouseEvent): void {
    const target = event.target instanceof Node ? event.target : null;
    const search = this.host.nativeElement.querySelector('.top-search-control');
    if (target && search?.contains(target)) {
      return;
    }

    this.quickOpen.set(false);
  }

  private hasQueryFlag(name: string): boolean {
    const query = this.currentUrl().split('?')[1] ?? '';
    return new URLSearchParams(query).get(name)?.toLowerCase() === 'true';
  }

  private hasQueryValue(name: string, value: string): boolean {
    const query = this.currentUrl().split('?')[1] ?? '';
    return new URLSearchParams(query).get(name)?.toLowerCase() === value.toLowerCase();
  }

  private runQuickSearch(query: string): void {
    this.api.fetchInventory({
      q: query,
      category: '',
      tagIds: [],
      box: '',
      boxIds: [],
      locationId: null,
      includeChildren: true,
      onlyConsumable: false,
      onlyOrphans: false,
      layout: 'flat',
      view: 'flat'
    }).pipe(
      tap((response) => {
        this.quickItems.set(response.items.slice(0, 8));
        this.quickBoxes.set(response.groups.filter((group) => group.boxId !== null).slice(0, 5));
      }),
      catchError(() => {
        this.quickItems.set([]);
        this.quickBoxes.set([]);
        return EMPTY;
      }),
      finalize(() => this.quickLoading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  private resetContentScroll(): void {
    window.requestAnimationFrame(() => {
      document.querySelector<HTMLElement>('.app-content')?.scrollTo({ top: 0, left: 0 });
      window.scrollTo({ top: 0, left: 0 });
    });
  }
}
