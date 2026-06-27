import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';

export interface HierarchyTrailNode {
  label: string;
  sublabel?: string | null;
  icon: string;
  routerLink?: string | unknown[] | null;
  tone?: 'location' | 'box' | 'item' | 'current' | 'muted';
  coverUrl?: string | null;
  rotationDegrees?: number;
}

@Component({
  selector: 'app-hierarchy-trail',
  standalone: true,
  imports: [CommonModule, RouterLink, MatIconModule],
  templateUrl: './hierarchy-trail.component.html',
  styleUrl: './hierarchy-trail.component.scss'
})
export class HierarchyTrailComponent {
  @Input() nodes: HierarchyTrailNode[] = [];
}
