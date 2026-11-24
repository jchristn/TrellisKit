#!/usr/bin/env node
// Generates the simple, original placeholder images and short video clips a seed file
// declares in `assets` (colored cards with text, initials avatars, and short clips with a
// moving bar). Uses ffmpeg on the host, or inside a running container (default: the local
// stack's server container, whose image includes ffmpeg) when the host has none.
//
// Usage: node generate-assets.mjs --file <seed.json> [--force] [--container trelliskit-server] [--font <ttf>]

import { execFileSync, spawnSync } from 'node:child_process';
import { mkdir, readFile, stat, writeFile, mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { parseArgs } from 'node:util';

const CONTAINER_FONT_DIR = '/usr/share/fonts/truetype/dejavu';

function hostHasFfmpeg() {
  return spawnSync('ffmpeg', ['-version'], { stdio: 'ignore' }).status === 0;
}

function hex(color, fallback) {
  const c = (color ?? fallback).replace('#', '');
  return `0x${c.length === 3 ? c.split('').map((x) => x + x).join('') : c}`;
}

function wrap(text, maxChars) {
  const words = String(text ?? '').split(/\s+/).filter(Boolean);
  const lines = [];
  let line = '';
  for (const w of words) {
    if ((line + ' ' + w).trim().length > maxChars && line) {
      lines.push(line);
      line = w;
    } else line = (line + ' ' + w).trim();
  }
  if (line) lines.push(line);
  return lines.join('\n');
}

function initials(name) {
  return String(name ?? '?').split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0].toUpperCase()).join('');
}

// Builds the ffmpeg arguments and the text files for one asset.
function plan(asset, fonts) {
  const w = asset.width ?? (asset.style === 'avatar' ? 400 : asset.kind === 'video' ? 960 : 1200);
  const h = asset.height ?? (asset.style === 'avatar' ? 400 : asset.kind === 'video' ? 540 : 800);
  const bg = hex(asset.background, '#334155');
  const fg = hex(asset.foreground, '#FFFFFF');
  const accent = hex(asset.accent, '#FFFFFF');
  const texts = {};
  const filters = [];
  if (asset.style === 'avatar') {
    texts.t1 = initials(asset.title);
    filters.push(`drawbox=x=0:y=0:w=iw:h=ih:color=${accent}@0.18:t=24`);
    filters.push(`drawtext=fontfile=${fonts.bold}:textfile={t1}:fontcolor=${fg}:fontsize=${Math.round(h * 0.38)}:x=(w-text_w)/2:y=(h-text_h)/2`);
  } else {
    const titleSize = asset.titleSize ?? Math.round(h * 0.085);
    const maxChars = Math.max(10, Math.floor(w / (titleSize * 0.58)));
    texts.t1 = wrap(asset.title, maxChars);
    texts.t2 = wrap(asset.subtitle ?? '', Math.floor(maxChars * 1.6));
    texts.t3 = asset.footer ?? '';
    filters.push(`drawbox=x=0:y=0:w=iw:h=ih:color=${accent}@0.35:t=${Math.round(h * 0.02)}`);
    filters.push(`drawbox=x=0:y=ih*0.80:w=iw:h=ih*0.20:color=black@0.25:t=fill`);
    filters.push(`drawtext=fontfile=${fonts.bold}:textfile={t1}:fontcolor=${fg}:fontsize=${titleSize}:line_spacing=${Math.round(titleSize * 0.25)}:text_align=C:x=(w-text_w)/2:y=(h*0.42-text_h/2)`);
    if (texts.t2) filters.push(`drawtext=fontfile=${fonts.regular}:textfile={t2}:fontcolor=${fg}@0.9:fontsize=${Math.round(titleSize * 0.5)}:line_spacing=6:text_align=C:x=(w-text_w)/2:y=h*0.62`);
    if (texts.t3) filters.push(`drawtext=fontfile=${fonts.regular}:textfile={t3}:fontcolor=${fg}@0.85:fontsize=${Math.round(titleSize * 0.42)}:x=w*0.04:y=h*0.88`);
  }
  const ext = path.extname(asset.file).toLowerCase();
  if (asset.kind === 'video') {
    const seconds = asset.durationSeconds ?? 8;
    const bar = `color=c=${accent}:s=${Math.round(w * 0.12)}x${Math.round(h * 0.03)}:r=30`;
    return {
      texts,
      args: (t) => [
        '-y', '-hide_banner', '-loglevel', 'error',
        '-f', 'lavfi', '-i', `color=c=${bg}:s=${w}x${h}:r=30:d=${seconds}`,
        '-f', 'lavfi', '-i', `${bar}:d=${seconds}`,
        '-f', 'lavfi', '-i', 'anullsrc=r=44100:cl=stereo',
        '-filter_complex', `[0:v]${t(filters.join(','))}[bg];[bg][1:v]overlay=x='mod(t*${Math.round(w / 3)},W)':y=H*0.76[v]`,
        '-map', '[v]', '-map', '2:a', '-t', String(seconds), '-shortest',
        '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '30', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '64k', '-movflags', '+faststart',
      ],
    };
  }
  return {
    texts,
    args: (t) => [
      '-y', '-hide_banner', '-loglevel', 'error',
      '-f', 'lavfi', '-i', `color=c=${bg}:s=${w}x${h}`,
      '-frames:v', '1', '-vf', t(filters.join(',')),
      ...(ext === '.png' ? [] : ['-q:v', '3']),
    ],
  };
}

async function main() {
  const { values } = parseArgs({
    options: { file: { type: 'string' }, force: { type: 'boolean' }, container: { type: 'string' }, font: { type: 'string' }, 'font-bold': { type: 'string' }, only: { type: 'string' } },
  });
  if (!values.file) {
    console.log('Usage: node generate-assets.mjs --file <seed.json> [--force] [--container <name>] [--font <ttf> --font-bold <ttf>] [--only key,key]');
    process.exit(2);
  }
  const seedFile = path.resolve(values.file);
  const seedDir = path.dirname(seedFile);
  const seed = JSON.parse(await readFile(seedFile, 'utf8'));
  const only = values.only ? new Set(values.only.split(',')) : null;
  const useHost = !values.container && hostHasFfmpeg();
  const container = values.container ?? 'trelliskit-server';
  const fonts = useHost
    ? { regular: values.font ?? '/System/Library/Fonts/Supplemental/Arial.ttf', bold: values['font-bold'] ?? values.font ?? '/System/Library/Fonts/Supplemental/Arial Bold.ttf' }
    : { regular: `${CONTAINER_FONT_DIR}/DejaVuSans.ttf`, bold: `${CONTAINER_FONT_DIR}/DejaVuSans-Bold.ttf` };
  console.log(useHost ? 'Using host ffmpeg' : `Using ffmpeg in container ${container}`);
  const work = await mkdtemp(path.join(os.tmpdir(), 'tk-seed-assets-'));
  const remote = `/tmp/tk-seed-assets-${process.pid}`;
  if (!useHost) execFileSync('docker', ['exec', container, 'mkdir', '-p', remote]);

  let made = 0;
  let kept = 0;
  try {
    for (const asset of seed.assets ?? []) {
      if (only && !only.has(asset.key)) continue;
      const out = path.resolve(seedDir, asset.file);
      if (!values.force) {
        try { await stat(out); kept += 1; continue; } catch { /* generate */ }
      }
      await mkdir(path.dirname(out), { recursive: true });
      const p = plan(asset, fonts);
      const textPaths = {};
      for (const [k, v] of Object.entries(p.texts)) {
        const local = path.join(work, `${asset.key}-${k}.txt`);
        await writeFile(local, v);
        if (useHost) textPaths[k] = local;
        else {
          const r = `${remote}/${asset.key}-${k}.txt`;
          execFileSync('docker', ['cp', local, `${container}:${r}`]);
          textPaths[k] = r;
        }
      }
      const subst = (s) => s.replace(/\{(t\d)\}/g, (m, k) => `'${textPaths[k]}'`);
      const args = p.args(subst);
      if (useHost) {
        execFileSync('ffmpeg', [...args, out], { stdio: 'inherit' });
      } else {
        const r = `${remote}/${asset.key}${path.extname(out)}`;
        execFileSync('docker', ['exec', container, 'ffmpeg', ...args, r], { stdio: 'inherit' });
        execFileSync('docker', ['cp', `${container}:${r}`, out]);
      }
      made += 1;
      console.log(`  ${asset.kind} ${asset.file}`);
    }
  } finally {
    await rm(work, { recursive: true, force: true });
    if (!useHost) spawnSync('docker', ['exec', container, 'rm', '-rf', remote]);
  }
  console.log(`Generated ${made} asset(s); ${kept} already present (use --force to regenerate).`);
}

main().catch((err) => {
  console.error(`generate-assets failed: ${err.message}`);
  process.exit(1);
});
