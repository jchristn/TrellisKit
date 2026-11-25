import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { qrMatrix, qrRowRuns, qrSvgPath } from '../src/index.js';

const require = createRequire(import.meta.url);
let reference = null;
try { reference = require('qrcode'); } catch { reference = null; }

const sample = (length) => Array.from({ length }, (_, i) => String.fromCharCode(33 + ((i * 7) % 90))).join('');

test('the encoder matches a reference encoder module for module', { skip: !reference && 'qrcode is not installed' }, () => {
  for (const level of ['L', 'M', 'Q', 'H']) {
    for (const length of [1, 17, 90, 200, 800, 2300]) {
      for (const mask of [0, 5]) {
        let ref;
        try { ref = reference.create([{ data: sample(length), mode: 'byte' }], { errorCorrectionLevel: level, maskPattern: mask }); } catch { continue; }
        const mine = qrMatrix(sample(length), { errorCorrection: level, mask });
        assert.equal(mine.version, ref.version, level + ' ' + length);
        for (let r = 0; r < mine.size; r++) for (let c = 0; c < mine.size; c++) assert.equal(mine.modules[r][c], Boolean(ref.modules.get(r, c)));
      }
    }
  }
});

test('a check-in link fits a small code and renders as runs and an SVG path', () => {
  const qr = qrMatrix('https://example.test/app/check-in?t=VenueSession&id=ven_abc%3A3&d=2026-11-25&c=123456');
  assert.equal(qr.version, 6);
  assert.equal(qr.size, 41);
  assert.ok(qr.mask >= 0 && qr.mask <= 7);
  assert.equal(qr.modules[0][0], true);
  assert.equal(qr.modules[7][7], false);
  const runs = qrRowRuns(qr);
  assert.equal(runs[0].row, 0);
  assert.equal(runs[0].length, 7);
  assert.ok(qrSvgPath(qr).startsWith('M4 4h7v1h-7z'));
  assert.throws(() => qrMatrix(sample(4000)), RangeError);
  assert.equal(qrMatrix('é').version, 1);
});
