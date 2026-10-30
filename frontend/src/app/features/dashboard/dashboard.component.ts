import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { Organization, PagedResponse } from '../organizations/organization.models';
import { OrganizationService } from '../organizations/organization.service';
import { WorkspaceHeaderComponent } from '../../shared/workspace-header.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [RouterLink, WorkspaceHeaderComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.css',
})
export class DashboardComponent implements OnInit {
  private readonly organizationsApi = inject(OrganizationService);
  readonly organizations = signal<Organization[]>([]);
  readonly organizationCount = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal('');

  ngOnInit(): void {
    this.organizationsApi.list().pipe(finalize(() => this.loading.set(false))).subscribe({
      next: (response: PagedResponse<Organization>) => {
        this.organizations.set(response.items.slice(0, 4));
        this.organizationCount.set(response.totalCount);
      },
      error: () => this.errorMessage.set('Your workspaces could not be loaded. Try again from the workspaces page.'),
    });
  }

}
