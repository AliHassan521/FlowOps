import { DatePipe } from '@angular/common';
import { Component, HostListener, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Notification } from './notification.models';
import { NotificationService } from './notification.service';

@Component({
  selector: 'app-notification-bell', standalone: true, imports: [DatePipe],
  templateUrl: './notification-bell.component.html', styleUrl: './notification-bell.component.css',
})
export class NotificationBellComponent {
  readonly inbox = inject(NotificationService);
  private readonly router = inject(Router);
  readonly opened = signal(false);
  toggle(): void { this.opened.update(value => !value); if (this.opened()) this.inbox.reload(); }
  @HostListener('keydown.escape') close(): void { this.opened.set(false); }
  open(notification: Notification): void {
    if (!notification.readAtUtc) this.inbox.markRead(notification.id);
    this.opened.set(false);
    void this.router.navigate(['/organizations', notification.organizationId, 'projects', notification.projectId, 'work-items'],
      { queryParams: { workItemId: notification.workItemId } });
  }
}
