import { Component, computed, inject, input } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { NotificationBellComponent } from '../features/notifications/notification-bell.component';

@Component({
  selector: 'app-workspace-header',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, NotificationBellComponent],
  templateUrl: './workspace-header.component.html',
  styleUrl: './workspace-header.component.css',
})
export class WorkspaceHeaderComponent {
  private readonly router = inject(Router);
  readonly auth = inject(AuthService);
  readonly section = input<'overview' | 'workspaces'>('overview');
  readonly initials = computed(() => (this.auth.currentUser()?.fullName ?? 'FlowOps')
    .split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0].toUpperCase()).join(''));

  signOut(): void {
    this.auth.logout().subscribe({
      next: () => this.router.navigate(['/login']),
      error: () => this.router.navigate(['/login']),
    });
  }
}
