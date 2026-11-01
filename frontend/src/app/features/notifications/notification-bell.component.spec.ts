import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter, Router } from '@angular/router';
import { NotificationBellComponent } from './notification-bell.component';
import { NotificationService } from './notification.service';

describe('NotificationBellComponent', () => {
  const notification = { id: 'n1', organizationId: 'org1', projectId: 'project1', workItemId: 'item1',
    kind: 'WorkItem.Assigned', message: 'You were assigned: Ship safely', createdAtUtc: '2026-11-02T00:00:00Z', readAtUtc: null };
  let inbox: { items: ReturnType<typeof signal>; unreadCount: ReturnType<typeof signal>; [key: string]: unknown };
  beforeEach(() => {
    inbox = { items: signal([notification]), unreadCount: signal(1), unreadOnly: signal(false), loading: signal(false),
      status: signal('live'), error: signal(''), page: signal(1), pages: () => 1, reload: vi.fn(),
      markRead: vi.fn(), markAllRead: vi.fn(), movePage: vi.fn(), filterUnread: vi.fn() };
    TestBed.configureTestingModule({ imports: [NotificationBellComponent], providers: [provideRouter([]),
      { provide: NotificationService, useValue: inbox }] });
  });

  it('renders the unread count, opens the inbox, and closes with Escape', () => {
    const fixture = TestBed.createComponent(NotificationBellComponent); fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('.bell') as HTMLButtonElement;
    expect(button.getAttribute('aria-label')).toContain('1 unread');
    button.click(); fixture.detectChanges();
    expect(inbox['reload']).toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('.entry').textContent).toContain('Ship safely');
    fixture.nativeElement.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges(); expect(fixture.nativeElement.querySelector('.inbox')).toBeNull();
  });

  it('marks an unread entry read and navigates to its exact work item', () => {
    const fixture = TestBed.createComponent(NotificationBellComponent);
    const router = TestBed.inject(Router); const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    fixture.componentInstance.open(notification);
    expect(inbox['markRead']).toHaveBeenCalledWith('n1');
    expect(navigate).toHaveBeenCalledWith(['/organizations', 'org1', 'projects', 'project1', 'work-items'],
      { queryParams: { workItemId: 'item1' } });
  });
});
