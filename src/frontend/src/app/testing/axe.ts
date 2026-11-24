import axe from 'axe-core';

/**
 * Runs axe-core (WCAG 2.2 A/AA rules) on a rendered component and fails with the list of violations.
 * Colour contrast cannot be computed in jsdom (no layout); Lighthouse checks it on the public pages.
 */
export async function expectAccessible(element: Element): Promise<void> {
  const result = await axe.run(element, {
    runOnly: {
      type: 'tag',
      values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa', 'best-practice'],
    },
    rules: {
      'color-contrast': { enabled: false },
      // A component is checked on its own, not as a whole page with landmarks and an <h1>.
      region: { enabled: false },
      'landmark-one-main': { enabled: false },
      'page-has-heading-one': { enabled: false },
    },
    resultTypes: ['violations'],
  });

  const violations = result.violations.map(
    (violation) =>
      `${violation.id}: ${violation.help} — ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
  );
  expect(violations).toEqual([]);
}
