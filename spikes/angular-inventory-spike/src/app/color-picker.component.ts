import { CommonModule } from '@angular/common';
import { Component, ElementRef, EventEmitter, HostListener, Input, Output, ViewChild, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-color-picker',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './color-picker.component.html',
  styleUrl: './color-picker.component.scss'
})
export class ColorPickerComponent {
  @Input() label = 'Color';
  @Input() set value(value: string | null | undefined) {
    this.applyHex(value || '#48ffb0', false);
  }

  @Output() readonly valueChange = new EventEmitter<string>();

  @ViewChild('plane') private readonly plane?: ElementRef<HTMLDivElement>;

  protected readonly hue = signal(150);
  protected readonly saturation = signal(100);
  protected readonly lightness = signal(64);
  protected readonly hex = signal('#48ffb0');
  protected readonly red = signal(72);
  protected readonly green = signal(255);
  protected readonly blue = signal(176);

  private draggingPlane = false;

  protected readonly planeBackground = () =>
    `linear-gradient(to top, #000, transparent), linear-gradient(to right, #fff, hsl(${this.hue()} 100% 50%))`;

  protected onPlanePointer(event: PointerEvent): void {
    this.draggingPlane = true;
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    this.pickPlane(event);
  }

  protected onHueChange(value: string | number): void {
    this.hue.set(this.clamp(Number(value), 0, 360));
    this.syncFromHsl(true);
  }

  protected onHexChange(value: string): void {
    const normalized = this.normalizeHex(value);
    this.hex.set(value);
    if (normalized) {
      this.applyHex(normalized, true);
    }
  }

  protected onRgbChange(channel: 'red' | 'green' | 'blue', value: string | number): void {
    const next = this.clamp(Number(value), 0, 255);
    if (channel === 'red') {
      this.red.set(next);
    } else if (channel === 'green') {
      this.green.set(next);
    } else {
      this.blue.set(next);
    }

    const hex = this.rgbToHex(this.red(), this.green(), this.blue());
    this.applyHex(hex, true);
  }

  @HostListener('document:pointerup')
  protected stopDragging(): void {
    this.draggingPlane = false;
  }

  @HostListener('document:pointermove', ['$event'])
  protected continueDragging(event: PointerEvent): void {
    if (this.draggingPlane) {
      this.pickPlane(event);
    }
  }

  private pickPlane(event: PointerEvent): void {
    const rect = this.plane?.nativeElement.getBoundingClientRect();
    if (!rect) {
      return;
    }

    const x = this.clamp(event.clientX - rect.left, 0, rect.width);
    const y = this.clamp(event.clientY - rect.top, 0, rect.height);
    this.saturation.set(Math.round((x / rect.width) * 100));
    this.lightness.set(Math.round((1 - y / rect.height) * 100));
    this.syncFromHsl(true);
  }

  private applyHex(value: string, emit: boolean): void {
    const normalized = this.normalizeHex(value);
    if (!normalized) {
      return;
    }

    const rgb = this.hexToRgb(normalized);
    const hsl = this.rgbToHsl(rgb.r, rgb.g, rgb.b);
    this.hex.set(normalized);
    this.red.set(rgb.r);
    this.green.set(rgb.g);
    this.blue.set(rgb.b);
    this.hue.set(hsl.h);
    this.saturation.set(hsl.s);
    this.lightness.set(hsl.l);
    if (emit) {
      this.valueChange.emit(normalized);
    }
  }

  private syncFromHsl(emit: boolean): void {
    const rgb = this.hslToRgb(this.hue(), this.saturation(), this.lightness());
    const hex = this.rgbToHex(rgb.r, rgb.g, rgb.b);
    this.hex.set(hex);
    this.red.set(rgb.r);
    this.green.set(rgb.g);
    this.blue.set(rgb.b);
    if (emit) {
      this.valueChange.emit(hex);
    }
  }

  private normalizeHex(value: string): string | null {
    const raw = value.trim();
    const expanded = /^#[0-9a-fA-F]{3}$/.test(raw)
      ? `#${raw[1]}${raw[1]}${raw[2]}${raw[2]}${raw[3]}${raw[3]}`
      : raw;

    return /^#[0-9a-fA-F]{6}$/.test(expanded) ? expanded.toLowerCase() : null;
  }

  private hexToRgb(hex: string): { r: number; g: number; b: number } {
    return {
      r: Number.parseInt(hex.slice(1, 3), 16),
      g: Number.parseInt(hex.slice(3, 5), 16),
      b: Number.parseInt(hex.slice(5, 7), 16)
    };
  }

  private rgbToHex(r: number, g: number, b: number): string {
    return `#${[r, g, b].map((value) => this.clamp(Math.round(value), 0, 255).toString(16).padStart(2, '0')).join('')}`;
  }

  private rgbToHsl(r: number, g: number, b: number): { h: number; s: number; l: number } {
    r /= 255;
    g /= 255;
    b /= 255;
    const max = Math.max(r, g, b);
    const min = Math.min(r, g, b);
    const l = (max + min) / 2;
    if (max === min) {
      return { h: 0, s: 0, l: Math.round(l * 100) };
    }

    const d = max - min;
    const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
    let h = 0;
    if (max === r) {
      h = (g - b) / d + (g < b ? 6 : 0);
    } else if (max === g) {
      h = (b - r) / d + 2;
    } else {
      h = (r - g) / d + 4;
    }

    return { h: Math.round(h * 60), s: Math.round(s * 100), l: Math.round(l * 100) };
  }

  private hslToRgb(h: number, s: number, l: number): { r: number; g: number; b: number } {
    h = this.clamp(h, 0, 360) / 360;
    s = this.clamp(s, 0, 100) / 100;
    l = this.clamp(l, 0, 100) / 100;

    if (s === 0) {
      const value = Math.round(l * 255);
      return { r: value, g: value, b: value };
    }

    const hue2rgb = (p: number, q: number, t: number): number => {
      if (t < 0) {
        t += 1;
      }
      if (t > 1) {
        t -= 1;
      }
      if (t < 1 / 6) {
        return p + (q - p) * 6 * t;
      }
      if (t < 1 / 2) {
        return q;
      }
      if (t < 2 / 3) {
        return p + (q - p) * (2 / 3 - t) * 6;
      }
      return p;
    };

    const q = l < 0.5 ? l * (1 + s) : l + s - l * s;
    const p = 2 * l - q;
    return {
      r: Math.round(hue2rgb(p, q, h + 1 / 3) * 255),
      g: Math.round(hue2rgb(p, q, h) * 255),
      b: Math.round(hue2rgb(p, q, h - 1 / 3) * 255)
    };
  }

  private clamp(value: number, min: number, max: number): number {
    if (!Number.isFinite(value)) {
      return min;
    }

    return Math.min(max, Math.max(min, value));
  }
}
