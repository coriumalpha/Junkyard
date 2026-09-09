import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, EMPTY, finalize, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';

import { AiConnectionTestResponse, AiSettingsUpdate, AiStatus, InventoryApiService } from './inventory-api.service';

type ImageDetail = 'low' | 'high' | 'auto';
type AiMode = 'normal' | 'cheap' | 'pro';

@Component({
  selector: 'app-settings-ai-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatSlideToggleModule
  ],
  templateUrl: './settings-ai-page.component.html',
  styleUrl: './settings-ai-page.component.scss'
})
export class SettingsAiPageComponent {
  protected readonly status = signal<AiStatus | null>(null);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly testing = signal(false);
  protected readonly keySaving = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly testResult = signal<AiConnectionTestResponse | null>(null);
  protected readonly apiKeyDraft = signal('');
  protected readonly replacingKey = signal(false);
  protected readonly enabled = signal(false);
  protected readonly provider = signal('OpenAI');
  protected readonly model = signal('gpt-5.4-mini');
  protected readonly proModel = signal('gpt-6-astra');
  protected readonly cheapModel = signal('gpt-5.4-nano');
  protected readonly imageDetail = signal<ImageDetail>('low');
  protected readonly maxImagesPerRequest = signal(4);
  protected readonly defaultMode = signal<AiMode>('normal');
  protected readonly effectiveState = computed(() => {
    const current = this.status();
    if (!current) {
      return 'Cargando';
    }
    if (!current.enabled) {
      return 'Desactivada';
    }
    if (!current.hasApiKey) {
      return 'Falta API key';
    }
    return current.isUsable ? 'Activa' : 'Revisar';
  });

  private readonly api = inject(InventoryApiService);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.fetchAiStatus().pipe(
      tap((status) => this.applyStatus(status)),
      catchError((error: unknown) => {
        this.error.set(this.errorMessage(error, 'No se pudo cargar configuración IA.'));
        return EMPTY;
      }),
      finalize(() => this.loading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected saveSettings(): void {
    this.saving.set(true);
    this.error.set(null);
    this.message.set(null);
    const input: AiSettingsUpdate = {
      enabled: this.enabled(),
      provider: this.provider(),
      model: this.model().trim(),
      cheapModel: this.cheapModel().trim(),
      proModel: this.proModel().trim(),
      imageDetail: this.imageDetail(),
      maxImagesPerRequest: Number(this.maxImagesPerRequest()),
      defaultMode: this.defaultMode()
    };
    this.api.updateAiSettings(input).pipe(
      tap((status) => {
        this.applyStatus(status);
        this.message.set('Configuración IA guardada.');
      }),
      catchError((error: unknown) => {
        this.error.set(this.errorMessage(error, 'No se pudo guardar configuración IA.'));
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected restoreDefaults(): void {
    this.enabled.set(false);
    this.provider.set('OpenAI');
    this.model.set('gpt-5.4-mini');
    this.cheapModel.set('gpt-5.4-nano');
    this.imageDetail.set('low');
    this.maxImagesPerRequest.set(4);
    this.defaultMode.set('normal');
  }

  protected saveApiKey(): void {
    const key = this.apiKeyDraft().trim();
    if (!key) {
      this.error.set('La API key no puede estar vacía.');
      return;
    }

    this.keySaving.set(true);
    this.error.set(null);
    this.message.set(null);
    this.api.saveAiApiKey(key).pipe(
      tap((status) => {
        this.apiKeyDraft.set('');
        this.replacingKey.set(false);
        this.applyStatus(status);
        this.message.set('API key guardada cifrada en la app.');
      }),
      catchError((error: unknown) => {
        this.error.set(this.errorMessage(error, 'No se pudo guardar la API key.'));
        return EMPTY;
      }),
      finalize(() => this.keySaving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected deleteApiKey(): void {
    this.keySaving.set(true);
    this.error.set(null);
    this.message.set(null);
    this.api.deleteAiApiKey().pipe(
      tap((status) => {
        this.apiKeyDraft.set('');
        this.replacingKey.set(false);
        this.applyStatus(status);
        this.message.set('API key guardada eliminada.');
      }),
      catchError((error: unknown) => {
        this.error.set(this.errorMessage(error, 'No se pudo borrar la API key.'));
        return EMPTY;
      }),
      finalize(() => this.keySaving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected testConnection(): void {
    this.testing.set(true);
    this.error.set(null);
    this.message.set(null);
    this.testResult.set(null);
    this.api.testAiConnection(this.defaultMode()).pipe(
      tap((result) => this.testResult.set(result)),
      catchError((error: unknown) => {
        this.error.set(this.errorMessage(error, 'No se pudo probar conexión IA.'));
        return EMPTY;
      }),
      finalize(() => this.testing.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected keySourceLabel(status: AiStatus | null): string {
    switch (status?.keySource) {
      case 'Environment':
        return 'Configurada por variable de entorno';
      case 'Stored':
        return 'Configurada en la app';
      case 'EnvironmentOverridesStored':
        return 'Entorno activo; existe key guardada en app';
      default:
        return 'No configurada';
    }
  }

  protected canEditStoredKey(status: AiStatus | null): boolean {
    return status?.keySource !== 'Environment';
  }

  private applyStatus(status: AiStatus): void {
    this.status.set(status);
    this.enabled.set(status.enabled);
    this.provider.set(status.provider);
    this.model.set(status.model);
    this.cheapModel.set(status.cheapModel);
    this.proModel.set(status.proModel ?? 'gpt-6-astra');
    this.imageDetail.set(status.imageDetail);
    this.maxImagesPerRequest.set(status.maxImagesPerRequest);
    this.defaultMode.set(status.defaultMode);
  }

  private errorMessage(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      const body = error.error as { error?: string; message?: string; details?: string } | string | null;
      if (typeof body === 'string') {
        const trimmed = body.trim();
        if (trimmed.startsWith('<!DOCTYPE html') || trimmed.startsWith('<html')) {
          return fallback;
        }
        if (trimmed) {
          return trimmed;
        }
      }
      if (body && typeof body === 'object') {
        const main = body.message || body.error;
        const details = body.details;
        if (main?.trim() && details?.trim()) {
          return `${main} ${details}`;
        }
        if (main?.trim()) {
          return main;
        }
      }
    }

    return error instanceof Error ? error.message : fallback;
  }
}
