#!/usr/bin/env node
// TrellisKit demo data seeder. Reads a property-owned seed file and creates the content
// through the public REST API only. Idempotent: every seeded object carries the tag
// `<namespace>: <key>` (namespace "seed" by default) and is looked up before it is created.
//
// Usage: node seed.mjs --file <property>/seed/demo.json [options]   (see README.md)

import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { Api } from './lib/client.mjs';
import { Context } from './lib/context.mjs';
import { Mailpit } from './lib/mailpit.mjs';
import { makeLog } from './lib/util.mjs';
import { validateSeed } from './lib/validate.mjs';
import { writeAccounts } from './lib/steps/report.mjs';

const STEPS = [
  ['codes', () => import('./lib/steps/codes.mjs'), 'seedCodes'],
  ['accounts', () => import('./lib/steps/accounts.mjs'), 'seedAccounts'],
  ['forums', () => import('./lib/steps/forums.mjs'), 'seedForums'],
  ['feed', () => import('./lib/steps/feed.mjs'), 'seedFeed'],
  ['threads', () => import('./lib/steps/threads.mjs'), 'seedThreads'],
  ['directory', () => import('./lib/steps/directory.mjs'), 'seedDirectory'],
  ['events', () => import('./lib/steps/events.mjs'), 'seedEvents'],
  ['library', () => import('./lib/steps/library.mjs'), 'seedLibrary'],
  ['messages', () => import('./lib/steps/messages.mjs'), 'seedMessages'],
  ['support', () => import('./lib/steps/support.mjs'), 'seedSupport'],
  ['ideas', () => import('./lib/steps/ideas.mjs'), 'seedIdeas'],
  ['broadcasts', () => import('./lib/steps/broadcasts.mjs'), 'seedBroadcasts'],
  ['ai', () => import('./lib/steps/ai.mjs'), 'seedAi'],
];

const HELP = `TrellisKit demo data seeder

  node seed.mjs --file <seed.json> [options]

Options
  --file <path>          Seed file (required), for example <property>/seed/demo.json
  --api <url>            API base URL (default http://127.0.0.1:8000, or TK_SEED_API)
  --web <url>            Website base URL for the credentials file (default http://127.0.0.1:8080)
  --mailpit <url>        Mailpit base URL for verification and reset emails (default http://127.0.0.1:8025; "none" to skip)
  --app <bundle id>      Registered app id sent as X-TK-App (MultiProperty; TK_SEED_APP)
  --property <id>        Property id sent as X-TK-Property (MultiProperty; TK_SEED_PROPERTY; default the seed file's propertyId when --app is set)
  --admin-token <token>  Break-glass admin token (default TK_ADMIN_TOKEN or the seed file's accounts.adminTokenEnv)
  --only <steps>         Comma-separated steps to run (accounts always runs first when content needs it)
  --skip <steps>         Comma-separated steps to skip
  --reset-content        Delete (or cancel, retire, deactivate) every seeded object instead of seeding; accounts stay
  --validate             Check the seed file and exit (no network)
  --verbose              More logging
  --help                 This help

Steps: ${STEPS.map((s) => s[0]).join(', ')}`;

async function main() {
  const { values } = parseArgs({
    options: {
      file: { type: 'string' }, api: { type: 'string' }, web: { type: 'string' }, mailpit: { type: 'string' }, app: { type: 'string' }, property: { type: 'string' },
      'admin-token': { type: 'string' }, only: { type: 'string' }, skip: { type: 'string' },
      'reset-content': { type: 'boolean' }, validate: { type: 'boolean' }, verbose: { type: 'boolean' }, help: { type: 'boolean' },
    },
  });
  if (values.help || !values.file) {
    console.log(HELP);
    process.exit(values.help ? 0 : 2);
  }
  const log = makeLog(values.verbose);
  const seedFile = path.resolve(values.file);
  const seed = JSON.parse(await readFile(seedFile, 'utf8'));
  const problems = validateSeed(seed);
  if (problems.length) {
    for (const p of problems) log.warn(p);
    if (values.validate) process.exit(1);
    throw new Error(`The seed file has ${problems.length} problem(s)`);
  }
  if (values.validate) {
    log.info(`${seedFile}: valid (${(seed.members ?? []).length} members, ${(seed.feed ?? []).length} feed posts, ${(seed.threads ?? []).length} threads, ${(seed.events ?? []).length} events)`);
    return;
  }

  const apiBase = values.api ?? process.env.TK_SEED_API ?? 'http://127.0.0.1:8000';
  const webBase = values.web ?? process.env.TK_SEED_WEB ?? 'http://127.0.0.1:8080';
  const mailpitBase = values.mailpit === 'none' ? null : values.mailpit ?? process.env.TK_SEED_MAILPIT ?? 'http://127.0.0.1:8025';
  const tokenEnv = seed.accounts?.adminTokenEnv ?? 'TK_ADMIN_TOKEN';
  const adminToken = values['admin-token'] ?? process.env[tokenEnv] ?? process.env.TK_ADMIN_TOKEN;
  if (!adminToken) throw new Error(`No admin token: pass --admin-token or set ${tokenEnv}`);

  const appId = values.app ?? process.env.TK_SEED_APP ?? null;
  const propertyHeader = values.property ?? process.env.TK_SEED_PROPERTY ?? (appId ? seed.propertyId ?? null : null);
  const api = new Api({ baseUrl: apiBase, adminToken, log, appId, propertyId: propertyHeader });
  const mailpit = new Mailpit(mailpitBase, log);
  const ctx = new Context({ api, mailpit, log, seed, seedFile, webBaseUrl: webBase, options: values });

  const health = await api.get('/api/v1/health', { auth: null });
  ctx.property = await api.get('/api/v1/property', { auth: null });
  log.info(`API ${apiBase}: ${health.status}; property ${ctx.property.name} (${ctx.property.id}); seed namespace "${ctx.namespace}"`);
  if (seed.propertyId && seed.propertyId !== ctx.property.id) {
    throw new Error(`The seed file is for property ${seed.propertyId} but the API serves ${ctx.property.id}. Start the stack for that property or pass --api.`);
  }
  if (mailpit.enabled && !(await mailpit.available())) {
    log.warn(`Mailpit at ${mailpitBase} is not reachable; emails will not be verified`);
    mailpit.baseUrl = null;
  }

  const only = values.only ? new Set(values.only.split(',').map((s) => s.trim())) : null;
  const skip = new Set((values.skip ?? '').split(',').map((s) => s.trim()).filter(Boolean));
  const started = Date.now();

  if (values['reset-content']) {
    const { resetContent } = await import('./lib/reset.mjs');
    await resetContent(ctx, { only, skip });
  } else {
    for (const [name, load, fn] of STEPS) {
      const wanted = (!only || only.has(name) || name === 'accounts' || name === 'codes') && !skip.has(name);
      if (!wanted) continue;
      const mod = await load();
      const t0 = Date.now();
      try {
        await mod[fn](ctx);
      } catch (err) {
        if (name === 'accounts' || name === 'codes') throw err;
        ctx.warn(`step ${name} failed: ${err.message}`);
        if (values.verbose) console.error(err);
      }
      log.debug(`step ${name} took ${((Date.now() - t0) / 1000).toFixed(1)}s`);
    }
    if (ctx.members.size) await writeAccounts(ctx);
  }

  printSummary(ctx, started);
}

function printSummary(ctx, started) {
  const rows = [...ctx.counts.entries()].map(([k, n]) => {
    const [type, outcome] = k.split('|');
    return { type, outcome, n };
  }).sort((a, b) => a.type.localeCompare(b.type) || a.outcome.localeCompare(b.outcome));
  console.log('');
  console.log(`Summary (${((Date.now() - started) / 1000).toFixed(0)}s, ${ctx.api.requestCount} requests, ${ctx.api.rateLimitWaits} rate limit waits)`);
  const width = Math.max(10, ...rows.map((r) => r.type.length));
  for (const r of rows) console.log(`  ${r.type.padEnd(width)}  ${String(r.n).padStart(4)}  ${r.outcome}`);
  if (ctx.credentialsFile && ctx.members.size) console.log(`\nCredentials: ${ctx.credentialsFile}`);
  if (ctx.gaps.length) {
    console.log('\nAPI gaps noticed:');
    for (const g of ctx.gaps) console.log(`  - ${g}`);
  }
  if (ctx.warnings.length) {
    console.log(`\n${ctx.warnings.length} warning(s):`);
    for (const w of ctx.warnings) console.log(`  - ${w}`);
  }
}

main().catch((err) => {
  console.error(`seed failed: ${err.message}`);
  if (process.env.TK_SEED_DEBUG) console.error(err);
  process.exit(1);
});
