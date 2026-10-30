import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';

function passwordsMatch(control: AbstractControl): ValidationErrors | null {
  const password = control.get('password')?.value;
  const confirmation = control.get('confirmPassword')?.value;
  return !confirmation || password === confirmation ? null : { passwordMismatch: true };
}

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './register.component.html',
  styleUrl: './register.component.css',
})
export class RegisterComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly passwordVisible = signal(false);
  readonly confirmationVisible = signal(false);
  readonly form = this.formBuilder.nonNullable.group({
    fullName: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    password: ['', [Validators.required, Validators.minLength(12), Validators.maxLength(1024)]],
    confirmPassword: ['', Validators.required],
  }, { validators: passwordsMatch });

  togglePasswordVisibility(): void {
    this.passwordVisible.update((visible) => !visible);
  }

  toggleConfirmationVisibility(): void {
    this.confirmationVisible.update((visible) => !visible);
  }

  submit(): void {
    this.form.controls.fullName.setValue(this.form.controls.fullName.value.trim());
    this.form.controls.email.setValue(this.form.controls.email.value.trim());
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }

    const { fullName, email, password } = this.form.getRawValue();
    this.submitting.set(true);
    this.errorMessage.set(null);
    this.authService.register({ fullName: fullName.trim(), email: email.trim(), password }).subscribe({
      next: () => this.router.navigate(['/dashboard']),
      error: (error: { error?: { message?: string } }) => {
        this.errorMessage.set(error.error?.message ?? 'Unable to create your account. Please try again.');
        this.submitting.set(false);
      },
    });
  }
}
