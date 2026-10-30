import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { LoginComponent } from './features/auth/login/login.component';
import { RegisterComponent } from './features/auth/register/register.component';
import { DashboardComponent } from './features/dashboard/dashboard.component';
import { OrganizationsComponent } from './features/organizations/organizations.component';
import { WorkItemsComponent } from './features/work-items/work-items.component';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  { path: 'login', component: LoginComponent },
  { path: 'register', component: RegisterComponent },
  { path: 'dashboard', component: DashboardComponent, canActivate: [authGuard] },
  { path: 'organizations', component: OrganizationsComponent, canActivate: [authGuard] },
  { path: 'organizations/:organizationId/projects/:projectId/work-items', component: WorkItemsComponent, canActivate: [authGuard] },
  { path: '**', redirectTo: 'dashboard' },
];
