import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { RegisterComponent } from './register.component';

describe('RegisterComponent', () => {
  const session = {
    userId: 'user-id',
    email: 'person@example.com',
    fullName: 'Test Person',
    roles: ['Engineer'],
    accessToken: 'access-token',
    accessTokenExpiresAtUtc: '2026-10-27T01:00:00Z',
  };
  let register: ReturnType<typeof vi.fn>;
  let router: Router;

  beforeEach(async () => {
    register = vi.fn().mockReturnValue(of(session));
    await TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { register } },
      ],
    }).compileComponents();
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
  });

  it('requires matching passwords and backend-compatible password length', () => {
    const fixture = TestBed.createComponent(RegisterComponent);
    const component = fixture.componentInstance;
    component.form.setValue({
      fullName: 'Test Person',
      email: 'person@example.com',
      password: 'short',
      confirmPassword: 'different',
    });

    expect(component.form.invalid).toBe(true);
    component.submit();
    expect(register).not.toHaveBeenCalled();
    expect(component.form.controls.password.errors?.['minlength']).toBeTruthy();
    expect(component.form.errors?.['passwordMismatch']).toBe(true);
  });

  it('submits normalized identity data and navigates after registration', () => {
    const fixture = TestBed.createComponent(RegisterComponent);
    const component = fixture.componentInstance;
    component.form.setValue({
      fullName: '  Test Person  ',
      email: ' person@example.com ',
      password: 'correct horse battery',
      confirmPassword: 'correct horse battery',
    });

    component.submit();

    expect(register).toHaveBeenCalledWith({
      fullName: 'Test Person',
      email: 'person@example.com',
      password: 'correct horse battery',
    });
    expect(router.navigate).toHaveBeenCalledWith(['/dashboard']);
  });

  it('shows the server error and re-enables the form after failure', () => {
    register.mockReturnValue(throwError(() => ({ error: { message: 'An account with this email already exists.' } })));
    const fixture = TestBed.createComponent(RegisterComponent);
    const component = fixture.componentInstance;
    component.form.setValue({
      fullName: 'Test Person',
      email: 'person@example.com',
      password: 'correct horse battery',
      confirmPassword: 'correct horse battery',
    });

    component.submit();

    expect(component.errorMessage()).toBe('An account with this email already exists.');
    expect(component.submitting()).toBe(false);
    expect(router.navigate).not.toHaveBeenCalled();
  });
});
