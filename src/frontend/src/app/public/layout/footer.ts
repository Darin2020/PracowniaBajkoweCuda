import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LEGAL_DOCUMENTS } from '../../content/legal';
import { SITE } from '../../content/site';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  selector: 'app-footer',
  styleUrl: './footer.scss',
  template: `
    <footer class="footer">
      <div class="footer__inner container">
        <nav aria-label="Legal" i18n-aria-label="@@footer.legalLabel">
          <ul class="footer__links">
            @for (document of documents; track document.id) {
              <li>
                <a class="footer__link" [routerLink]="['/', document.id]">{{ document.title }}</a>
              </li>
            }
          </ul>
        </nav>
        <p class="footer__copyright">© {{ year }} {{ siteName }}</p>
      </div>
    </footer>
  `,
})
export class Footer {
  protected readonly siteName = SITE.name;
  protected readonly year = new Date().getFullYear();
  protected readonly documents = Object.values(LEGAL_DOCUMENTS);
}
