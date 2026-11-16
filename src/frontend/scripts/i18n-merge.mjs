// Keeps the translation files in step with the extracted source messages.
//
//   npm run i18n:extract                      extract src/locale/messages.xlf, then merge
//   node scripts/i18n-merge.mjs --seed <dir>  also take draft translations from <dir>/<locale>.json
//
// For every locale it rewrites src/locale/messages.<locale>.xlf so that it has exactly the units
// of messages.xlf, in the same order:
//   - an existing translation is kept (its state too) while the English source is unchanged;
//   - when the English source changed, the old translation is kept but flagged `needs-review-translation`;
//   - a missing translation is taken from the seed file if there is one (`needs-review-translation`),
//     otherwise the English text is copied and flagged `new`.
//
// Seed files map message ids to text. Placeholders are written as {NAME}, for example
// "Image {INDEX} of {TOTAL}", and are replaced with the matching <x id="NAME" .../> element.
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const LOCALES = ['de', 'fr', 'es', 'pl'];
const localeDir = join(import.meta.dirname, '..', 'src', 'locale');
const seedDir = process.argv.includes('--seed')
  ? process.argv[process.argv.indexOf('--seed') + 1]
  : undefined;

const UNIT = /<trans-unit id="([^"]+)"[^>]*>[\s\S]*?<\/trans-unit>/g;
const SOURCE = /<source>([\s\S]*?)<\/source>/;
const TARGET = /\s*<target(?: state="([^"]*)")?>([\s\S]*?)<\/target>/;

const escapeXml = (text) => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/** Turns seed text into XLIFF, reusing the placeholder elements of the source message. */
function seedToXliff(id, text, source) {
  const placeholders = new Map(
    [...source.matchAll(/<x id="([^"]+)"[^>]*\/>/g)].map((match) => [match[1], match[0]]),
  );
  const used = new Set();
  const xliff = escapeXml(text).replace(/\{([A-Z_0-9]+)\}/g, (_, name) => {
    if (!placeholders.has(name)) {
      throw new Error(`Seed for "${id}" uses unknown placeholder {${name}}.`);
    }
    used.add(name);
    return placeholders.get(name);
  });

  for (const name of placeholders.keys()) {
    if (!used.has(name)) {
      throw new Error(`Seed for "${id}" is missing placeholder {${name}}.`);
    }
  }

  return xliff;
}

function readUnits(file) {
  const units = new Map();

  if (existsSync(file)) {
    for (const [unit, id] of readFileSync(file, 'utf8').matchAll(UNIT)) {
      const target = unit.match(TARGET);
      units.set(id, { source: unit.match(SOURCE)?.[1], state: target?.[1], target: target?.[2] });
    }
  }

  return units;
}

const template = readFileSync(join(localeDir, 'messages.xlf'), 'utf8');
let exitCode = 0;

for (const locale of LOCALES) {
  const file = join(localeDir, `messages.${locale}.xlf`);
  const existing = readUnits(file);
  const seedFile = seedDir ? join(seedDir, `${locale}.json`) : undefined;
  const seed = seedFile && existsSync(seedFile) ? JSON.parse(readFileSync(seedFile, 'utf8')) : {};
  const counts = { kept: 0, changed: 0, seeded: 0, untranslated: 0 };

  const merged = template
    .replace('source-language="en"', `source-language="en" target-language="${locale}"`)
    .replace(UNIT, (unit, id) => {
      const source = unit.match(SOURCE)[1];
      const previous = existing.get(id);
      let target;
      let state;

      if (previous?.target !== undefined && previous.state !== 'new') {
        target = previous.target;
        state = previous.source === source ? previous.state : 'needs-review-translation';
        counts[previous.source === source ? 'kept' : 'changed']++;
      } else if (seed[id] !== undefined) {
        target = seedToXliff(id, seed[id], source);
        state = 'needs-review-translation';
        counts.seeded++;
      } else {
        target = source;
        state = 'new';
        counts.untranslated++;
      }

      const stateAttribute = state ? ` state="${state}"` : '';

      return unit.replace(
        /(\n(\s*))<source>[\s\S]*?<\/source>/,
        (sourceElement, _newline, indent) =>
          `${sourceElement}\n${indent}<target${stateAttribute}>${target}</target>`,
      );
    });

  writeFileSync(file, merged);
  console.log(
    `${locale}: ${counts.kept} kept, ${counts.changed} source changed, ` +
      `${counts.seeded} seeded, ${counts.untranslated} untranslated`,
  );

  if (counts.untranslated > 0) {
    exitCode = 1;
  }
}

if (exitCode !== 0) {
  console.error('Some messages have no translation (state="new"). Translate them before building.');
}

process.exitCode = exitCode;
