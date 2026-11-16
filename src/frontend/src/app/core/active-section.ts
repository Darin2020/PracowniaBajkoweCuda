import { Injectable, signal } from '@angular/core';

/** Id of the landing-page section currently in view (`about`, `gallery`, ...), or `null` elsewhere. */
@Injectable({ providedIn: 'root' })
export class ActiveSection {
  readonly id = signal<string | null>(null);
}
