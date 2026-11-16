import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SITE } from '../../content/site';
import { ActiveSection } from '../../core/active-section';
import { LanguageSwitcher } from './language-switcher';

interface NavItem {
  /** Id of the landing-page section the item scrolls to. */
  readonly id: string;
  readonly label: string;
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'closeMenu()' },
  imports: [LanguageSwitcher, RouterLink],
  selector: 'app-top-bar',
  styleUrl: './top-bar.scss',
  templateUrl: './top-bar.html',
})
export class TopBar {
  private readonly activeSection = inject(ActiveSection);

  protected readonly siteName = SITE.name;
  protected readonly activeId = this.activeSection.id;
  protected readonly menuOpen = signal(false);

  protected readonly navItems: readonly NavItem[] = [
    { id: 'about', label: $localize`:@@nav.about:About` },
    { id: 'gallery', label: $localize`:@@nav.gallery:Gallery` },
    { id: 'pricing', label: $localize`:@@nav.pricing:Pricing` },
    { id: 'contact', label: $localize`:@@nav.contact:Contact` },
  ];

  protected toggleMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  protected closeMenu(): void {
    this.menuOpen.set(false);
  }
}
