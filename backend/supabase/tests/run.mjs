#!/usr/bin/env node
// Convenience entry point (docs/ARCHITECTURE.md section 10): runs the in-memory
// pglite suite and, when SUPABASE_URL + SUPABASE_ANON_KEY are set, the REST
// suite against that stack. Exit code is non-zero when any suite fails.
//
//   node run.mjs          (or: npm run test:all)

import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));

function runSuite(file) {
  console.log(`\n[run] ${file}`);
  const result = spawnSync(process.execPath, ['--test', path.join(here, file)], { stdio: 'inherit' });
  return result.status === null ? 1 : result.status;
}

let status = runSuite('pglite.test.mjs');

if (process.env.SUPABASE_URL && process.env.SUPABASE_ANON_KEY) {
  status = Math.max(status, runSuite('rest.test.mjs'));
} else {
  console.log('\n[run] SUPABASE_URL / SUPABASE_ANON_KEY not set - REST suite skipped.');
}

process.exit(status);
