import { TestBed } from '@angular/core/testing';
import { LEGAL_DOCUMENTS, LegalDocumentId } from '../../content/legal';
import { PUBLIC_BASE_URL } from '../../core/public-base-url';
import { LegalPage } from './legal-page';
import { expectAccessible } from '../../testing/axe';

describe('LegalPage', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [LegalPage],
      providers: [{ provide: PUBLIC_BASE_URL, useValue: 'https://toys.test' }],
    });
  });

  function render(document: LegalDocumentId): HTMLElement {
    const fixture = TestBed.createComponent(LegalPage);
    fixture.componentRef.setInput('document', document);
    fixture.detectChanges();

    return fixture.nativeElement as HTMLElement;
  }

  it.each(['privacy', 'terms', 'cookies'] as const)('shows the %s document', (id) => {
    const element = render(id);

    expect(element.querySelector('h1')?.textContent).toBe(LEGAL_DOCUMENTS[id].title);
    expect(element.querySelectorAll('.legal__topics li')).toHaveLength(
      LEGAL_DOCUMENTS[id].topics.length,
    );
  });

  it('says clearly that the text is a placeholder', () => {
    expect(render('privacy').querySelector('[role="note"]')?.textContent).toContain('placeholder');
  });

  it('describes the page for search engines', () => {
    render('terms');
    TestBed.tick();

    expect(document.title).toBe('Terms of service · Pracownia Bajkowe Cuda');
    expect(document.head.querySelector('link[rel="canonical"]')?.getAttribute('href')).toBe(
      'https://toys.test/en/terms',
    );
  });

  it('has no accessibility violations', async () => {
    await expectAccessible(render('privacy'));
  });
});
