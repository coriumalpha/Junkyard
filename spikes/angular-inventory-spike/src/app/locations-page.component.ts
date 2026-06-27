import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, EMPTY, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { EntityMiniCardComponent } from './entity-mini-card.component';
import { InventoryApiService, InventoryBoxOption, InventoryOption, InventoryOptionsResponse } from './inventory-api.service';
import { legacyUrl } from './legacy-url';

interface LocationCard {
  location: InventoryOption;
  boxes: InventoryBoxOption[];
}

@Component({
  selector: 'app-locations-page',
  standalone: true,
  imports: [
    CommonModule,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatIconModule,
    MatProgressSpinnerModule,
    RouterLink,
    EntityMiniCardComponent
  ],
  templateUrl: './locations-page.component.html',
  styleUrl: './locations-page.component.scss'
})
export class LocationsPageComponent {
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly options = signal<InventoryOptionsResponse>({ categories: [], tags: [], locations: [], boxes: [] });
  protected readonly legacyLocationsUrl = legacyUrl('/Locations');
  protected readonly cards = computed<LocationCard[]>(() => {
    const boxes = this.options().boxes;
    return this.options().locations.map((location) => ({
      location,
      boxes: boxes.filter((box) => box.locationName === location.name)
    }));
  });

  private readonly api = inject(InventoryApiService);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    this.api.fetchOptions().pipe(
      tap((options) => {
        this.options.set(options);
        this.loading.set(false);
      }),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudieron cargar las ubicaciones.');
        this.loading.set(false);
        return EMPTY;
      }),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected assetUrl(path: string | null | undefined): string | null {
    if (!path) {
      return null;
    }

    return path.startsWith('/') ? path : `/${path}`;
  }
}
