import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-entity-mini-card',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './entity-mini-card.component.html',
  styleUrl: './entity-mini-card.component.scss'
})
export class EntityMiniCardComponent {
  @Input() title = '';
  @Input() subtitle: string | null = null;
  @Input() meta: string | null = null;
  @Input() imageUrl: string | null = null;
  @Input() rotationDegrees = 0;
  @Input() placeholder = '?';
  @Input() routerLink: string | unknown[] | null = null;
}
