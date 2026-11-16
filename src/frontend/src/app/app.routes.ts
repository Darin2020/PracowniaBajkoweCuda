import { Routes } from '@angular/router';
import { LegalDocumentId } from './content/legal';
import { LandingPage } from './public/landing/landing-page';

const legalRoute = (document: LegalDocumentId) => ({
  path: document,
  loadComponent: () => import('./public/legal/legal-page').then((m) => m.LegalPage),
  data: { document },
});

export const routes: Routes = [
  { path: '', pathMatch: 'full', component: LandingPage },
  legalRoute('privacy'),
  legalRoute('terms'),
  legalRoute('cookies'),
  // Accounts and the chat arrive in later phases. Until then their links lead to a short notice.
  {
    path: 'login',
    loadComponent: () => import('./public/pages/coming-soon-page').then((m) => m.ComingSoonPage),
  },
  { path: 'chat', redirectTo: 'login' },
  {
    path: '**',
    loadComponent: () => import('./public/pages/not-found-page').then((m) => m.NotFoundPage),
  },
];
