// `npm audit` that fails on high and critical advisories, except those accepted in audit-allowlist.json.
// An accepted advisory needs a reason and a review date; after that date the audit fails again until someone
// looks at it. Used by CI (`npm run audit`).

import { execSync } from 'node:child_process';
import { readFileSync } from 'node:fs';

const FAILING_SEVERITIES = new Set(['high', 'critical']);
const allowlistUrl = new URL('../audit-allowlist.json', import.meta.url);

/** @type {{ id: string, package: string, reason: string, reviewBy: string }[]} */
const allowlist = JSON.parse(readFileSync(allowlistUrl, 'utf8').replace(/^﻿/, ''));

function runAudit() {
  try {
    // A fixed command line, so running it through the shell is safe (npm is npm.cmd on Windows).
    return execSync('npm audit --json', { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  } catch (error) {
    // npm audit exits with 1 when it finds anything; the report is still on stdout.
    if (typeof error.stdout === 'string' && error.stdout.length > 0) {
      return error.stdout;
    }
    throw error;
  }
}

const report = JSON.parse(runAudit());
if (report.error) {
  console.error(`npm audit failed: ${report.error.summary ?? JSON.stringify(report.error)}`);
  process.exit(1);
}

// Advisories are the object entries of `via`; string entries only point at another vulnerable package.
const advisories = new Map();
for (const vulnerability of Object.values(report.vulnerabilities ?? {})) {
  for (const via of vulnerability.via) {
    if (typeof via === 'object' && FAILING_SEVERITIES.has(via.severity)) {
      const id = via.url.split('/').pop();
      advisories.set(id, {
        id,
        package: via.name,
        severity: via.severity,
        title: via.title,
        url: via.url,
      });
    }
  }
}

const today = new Date().toISOString().slice(0, 10);
const failures = [];

for (const advisory of advisories.values()) {
  const accepted = allowlist.find((entry) => entry.id === advisory.id);
  const line = `${advisory.severity} ${advisory.package}: ${advisory.title} (${advisory.url})`;

  if (!accepted) {
    failures.push(line);
  } else if (accepted.reviewBy < today) {
    failures.push(
      `${line}\n    accepted until ${accepted.reviewBy}: review it, then extend or remove the entry`,
    );
  } else {
    console.log(`accepted until ${accepted.reviewBy}: ${line}\n    reason: ${accepted.reason}`);
  }
}

for (const entry of allowlist) {
  if (!advisories.has(entry.id)) {
    console.log(
      `no longer reported, remove it from audit-allowlist.json: ${entry.id} (${entry.package})`,
    );
  }
}

if (failures.length > 0) {
  console.error(
    `\n${failures.length} high or critical advisories:\n${failures.map((f) => `  ${f}`).join('\n')}`,
  );
  process.exit(1);
}

console.log(
  `\nnpm audit: no unaccepted high or critical advisories (${advisories.size} accepted).`,
);
