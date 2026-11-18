import { Routes } from '@angular/router';
import { LegalDocumentId } from './content/legal';
import {
  adminAreaGuard,
  adminGuard,
  anonymousGuard,
  authGuard,
  homeGuard,
  sessionGuard,
} from './core/auth/auth.guards';
import { LandingPage } from './public/landing/landing-page';

const legalRoute = (document: LegalDocumentId) => ({
  path: document,
  loadComponent: () => import('./public/legal/legal-page').then((m) => m.LegalPage),
  data: { document },
});

// docs/PLAN.md §4.4. Only the landing and the legal pages are prerendered (app.routes.server.ts); every other page is
// client-rendered and may call the API.
export const routes: Routes = [
  { path: '', pathMatch: 'full', component: LandingPage },
  legalRoute('privacy'),
  legalRoute('terms'),
  legalRoute('cookies'),

  // Signing in and signing up.
  {
    path: 'login',
    pathMatch: 'full',
    canActivate: [anonymousGuard],
    loadComponent: () => import('./auth/login-page').then((m) => m.LoginPage),
  },
  {
    path: 'login/2fa',
    canActivate: [anonymousGuard],
    loadComponent: () => import('./auth/two-factor-login-page').then((m) => m.TwoFactorLoginPage),
  },
  {
    path: 'register',
    canActivate: [anonymousGuard],
    loadComponent: () => import('./auth/register-page').then((m) => m.RegisterPage),
  },
  {
    path: 'complete-profile',
    canActivate: [anonymousGuard],
    loadComponent: () => import('./auth/complete-profile-page').then((m) => m.CompleteProfilePage),
  },
  {
    path: 'forgot-password',
    canActivate: [anonymousGuard],
    loadComponent: () => import('./auth/forgot-password-page').then((m) => m.ForgotPasswordPage),
  },
  {
    path: 'reset-password',
    canActivate: [sessionGuard],
    loadComponent: () => import('./auth/reset-password-page').then((m) => m.ResetPasswordPage),
  },
  {
    path: 'confirm-email',
    canActivate: [sessionGuard],
    loadComponent: () => import('./auth/confirm-email-page').then((m) => m.ConfirmEmailPage),
  },

  // Signed-in area. `/signed-in` only redirects (see homeGuard).
  { path: 'signed-in', canActivate: [homeGuard], children: [] },
  {
    path: 'account',
    canActivate: [authGuard],
    loadComponent: () => import('./account/account-page').then((m) => m.AccountPage),
  },
  {
    path: 'account-deleted',
    loadComponent: () =>
      import('./public/pages/account-deleted-page').then((m) => m.AccountDeletedPage),
  },
  {
    path: 'chat',
    canActivate: [authGuard],
    loadComponent: () => import('./chat/chat-page').then((m) => m.ChatPage),
  },
  {
    path: 'admin/conversations',
    canActivate: [adminAreaGuard],
    loadComponent: () => import('./admin/conversations-page').then((m) => m.ConversationsPage),
  },
  {
    path: 'admin/conversations/:id',
    canActivate: [adminAreaGuard],
    loadComponent: () => import('./admin/conversation-page').then((m) => m.ConversationPage),
  },
  {
    path: 'admin/2fa-setup',
    canActivate: [adminGuard],
    loadComponent: () => import('./admin/two-factor-setup-page').then((m) => m.TwoFactorSetupPage),
  },

  {
    path: '**',
    loadComponent: () => import('./public/pages/not-found-page').then((m) => m.NotFoundPage),
  },
];
