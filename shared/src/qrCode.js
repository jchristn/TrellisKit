/**
 * A small dependency-free QR code encoder (ISO/IEC 18004, byte mode, versions 1 to 40) shared by
 * the web and mobile apps, so a client can draw a QR code without a native module: the web app
 * renders `qrSvgPath` in an SVG and the mobile app draws `qrRowRuns` with plain views.
 *
 * Usage: `const qr = qrMatrix('https://example.test/path')` returns `{ size, version, mask, modules }`
 * where `modules[row][col]` is true for a dark module. The quiet zone is not included.
 */

/** Error correction levels and their two-bit format values. */
export const QrErrorCorrection = Object.freeze({ L: 'L', M: 'M', Q: 'Q', H: 'H' });

const FORMAT_BITS = { L: 1, M: 0, Q: 3, H: 2 };
const ECL_INDEX = { L: 0, M: 1, Q: 2, H: 3 };

// Error correction codewords per block, by level then version (index 0 unused).
const ECC_PER_BLOCK = [
  [-1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
  [-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28],
  [-1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
  [-1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30]
];

// Number of error correction blocks, by level then version (index 0 unused).
const ECC_BLOCKS = [
  [-1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25],
  [-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49],
  [-1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68],
  [-1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81]
];

function utf8Bytes(text) {
  const out = [];
  const s = String(text);
  for (let i = 0; i < s.length; i++) {
    let c = s.charCodeAt(i);
    if (c >= 0xd800 && c <= 0xdbff && i + 1 < s.length) {
      const d = s.charCodeAt(i + 1);
      if (d >= 0xdc00 && d <= 0xdfff) { c = 0x10000 + ((c - 0xd800) << 10) + (d - 0xdc00); i++; }
    }
    if (c < 0x80) out.push(c);
    else if (c < 0x800) out.push(0xc0 | (c >> 6), 0x80 | (c & 63));
    else if (c < 0x10000) out.push(0xe0 | (c >> 12), 0x80 | ((c >> 6) & 63), 0x80 | (c & 63));
    else out.push(0xf0 | (c >> 18), 0x80 | ((c >> 12) & 63), 0x80 | ((c >> 6) & 63), 0x80 | (c & 63));
  }
  return out;
}

function rawDataModules(ver) {
  let result = (16 * ver + 128) * ver + 64;
  if (ver >= 2) {
    const align = Math.floor(ver / 7) + 2;
    result -= (25 * align - 10) * align - 55;
    if (ver >= 7) result -= 36;
  }
  return result;
}

function dataCodewords(ver, ecl) {
  const e = ECL_INDEX[ecl];
  return Math.floor(rawDataModules(ver) / 8) - ECC_PER_BLOCK[e][ver] * ECC_BLOCKS[e][ver];
}

function gfMultiply(x, y) {
  let z = 0;
  for (let i = 7; i >= 0; i--) {
    z = (z << 1) ^ ((z >>> 7) * 0x11d);
    z ^= ((y >>> i) & 1) * x;
  }
  return z & 0xff;
}

function rsDivisor(degree) {
  const result = new Array(degree).fill(0);
  result[degree - 1] = 1;
  let root = 1;
  for (let i = 0; i < degree; i++) {
    for (let j = 0; j < result.length; j++) {
      result[j] = gfMultiply(result[j], root);
      if (j + 1 < result.length) result[j] ^= result[j + 1];
    }
    root = gfMultiply(root, 0x02);
  }
  return result;
}

function rsRemainder(data, divisor) {
  const result = divisor.map(() => 0);
  for (const b of data) {
    const factor = b ^ result.shift();
    result.push(0);
    for (let i = 0; i < result.length; i++) result[i] ^= gfMultiply(divisor[i], factor);
  }
  return result;
}

function alignmentPositions(ver) {
  if (ver === 1) return [];
  const count = Math.floor(ver / 7) + 2;
  const size = ver * 4 + 17;
  const step = ver === 32 ? 26 : Math.ceil((ver * 4 + 4) / (count * 2 - 2)) * 2;
  const result = [6];
  for (let pos = size - 7; result.length < count; pos -= step) result.splice(1, 0, pos);
  return result;
}

const MASKS = [
  (x, y) => (x + y) % 2 === 0,
  (x, y) => y % 2 === 0,
  (x) => x % 3 === 0,
  (x, y) => (x + y) % 3 === 0,
  (x, y) => (Math.floor(x / 3) + Math.floor(y / 2)) % 2 === 0,
  (x, y) => ((x * y) % 2) + ((x * y) % 3) === 0,
  (x, y) => (((x * y) % 2) + ((x * y) % 3)) % 2 === 0,
  (x, y) => (((x + y) % 2) + ((x * y) % 3)) % 2 === 0
];

function build(ver, ecl, codewords, mask) {
  const size = ver * 4 + 17;
  const modules = Array.from({ length: size }, () => new Array(size).fill(false));
  const fn = Array.from({ length: size }, () => new Array(size).fill(false));
  const set = (x, y, dark) => { modules[y][x] = dark; fn[y][x] = true; };

  for (let i = 0; i < size; i++) { set(6, i, i % 2 === 0); set(i, 6, i % 2 === 0); }
  const finder = (cx, cy) => {
    for (let dy = -4; dy <= 4; dy++) {
      for (let dx = -4; dx <= 4; dx++) {
        const dist = Math.max(Math.abs(dx), Math.abs(dy));
        const x = cx + dx;
        const y = cy + dy;
        if (x >= 0 && x < size && y >= 0 && y < size) set(x, y, dist !== 2 && dist !== 4);
      }
    }
  };
  finder(3, 3); finder(size - 4, 3); finder(3, size - 4);
  const positions = alignmentPositions(ver);
  const last = positions.length - 1;
  for (let i = 0; i < positions.length; i++) {
    for (let j = 0; j < positions.length; j++) {
      if ((i === 0 && j === 0) || (i === 0 && j === last) || (i === last && j === 0)) continue;
      for (let dy = -2; dy <= 2; dy++) for (let dx = -2; dx <= 2; dx++) set(positions[i] + dx, positions[j] + dy, Math.max(Math.abs(dx), Math.abs(dy)) !== 1);
    }
  }
  const drawFormat = (m) => {
    const data = (FORMAT_BITS[ecl] << 3) | m;
    let rem = data;
    for (let i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >>> 9) * 0x537);
    const bits = ((data << 10) | rem) ^ 0x5412;
    const bit = (i) => ((bits >>> i) & 1) !== 0;
    for (let i = 0; i <= 5; i++) set(8, i, bit(i));
    set(8, 7, bit(6)); set(8, 8, bit(7)); set(7, 8, bit(8));
    for (let i = 9; i < 15; i++) set(14 - i, 8, bit(i));
    for (let i = 0; i < 8; i++) set(size - 1 - i, 8, bit(i));
    for (let i = 8; i < 15; i++) set(8, size - 15 + i, bit(i));
    set(8, size - 8, true);
  };
  drawFormat(0);
  if (ver >= 7) {
    let rem = ver;
    for (let i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >>> 11) * 0x1f25);
    const bits = (ver << 12) | rem;
    for (let i = 0; i < 18; i++) {
      const dark = ((bits >>> i) & 1) !== 0;
      const a = size - 11 + (i % 3);
      const b = Math.floor(i / 3);
      set(a, b, dark); set(b, a, dark);
    }
  }

  let i = 0;
  for (let right = size - 1; right >= 1; right -= 2) {
    if (right === 6) right = 5;
    for (let vert = 0; vert < size; vert++) {
      for (let j = 0; j < 2; j++) {
        const x = right - j;
        const upward = ((right + 1) & 2) === 0;
        const y = upward ? size - 1 - vert : vert;
        if (!fn[y][x] && i < codewords.length * 8) {
          modules[y][x] = ((codewords[i >>> 3] >>> (7 - (i & 7))) & 1) !== 0;
          i++;
        }
      }
    }
  }

  const apply = (m) => {
    const out = modules.map((row) => row.slice());
    for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) if (!fn[y][x] && MASKS[m](x, y)) out[y][x] = !out[y][x];
    return out;
  };
  const withFormat = (grid, m) => {
    const saved = modules.map((row) => row.slice());
    for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) modules[y][x] = grid[y][x];
    drawFormat(m);
    const result = modules.map((row) => row.slice());
    for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) modules[y][x] = saved[y][x];
    return result;
  };

  if (mask !== null) return { size, mask, modules: withFormat(apply(mask), mask) };
  let best = null;
  for (let m = 0; m < 8; m++) {
    const grid = withFormat(apply(m), m);
    const score = penalty(grid);
    if (!best || score < best.score) best = { score, mask: m, modules: grid };
  }
  return { size, mask: best.mask, modules: best.modules };
}

/** The standard penalty score (runs, 2x2 blocks, finder-like patterns, dark balance). */
function penalty(grid) {
  const size = grid.length;
  let score = 0;
  const lines = [];
  for (let y = 0; y < size; y++) lines.push(grid[y]);
  for (let x = 0; x < size; x++) lines.push(grid.map((row) => row[x]));
  for (const line of lines) {
    let run = 1;
    for (let i = 1; i <= size; i++) {
      if (i < size && line[i] === line[i - 1]) run++;
      else { if (run >= 5) score += 3 + (run - 5); run = 1; }
    }
    const text = line.map((d) => (d ? '1' : '0')).join('');
    for (const pattern of ['10111010000', '00001011101']) {
      let at = text.indexOf(pattern);
      while (at >= 0) { score += 40; at = text.indexOf(pattern, at + 1); }
    }
  }
  let dark = 0;
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      if (grid[y][x]) dark++;
      if (x < size - 1 && y < size - 1) {
        const c = grid[y][x];
        if (grid[y][x + 1] === c && grid[y + 1][x] === c && grid[y + 1][x + 1] === c) score += 3;
      }
    }
  }
  const total = size * size;
  score += Math.floor(Math.abs(dark * 20 - total * 10) / total) * 10;
  return score;
}

/**
 * Encode text (UTF-8, byte mode) as a QR code.
 * @param {string} text
 * @param {{ errorCorrection?: 'L'|'M'|'Q'|'H', mask?: number, minVersion?: number }} [options]
 * @returns {{ size: number, version: number, mask: number, errorCorrection: string, modules: boolean[][] }}
 * @throws {RangeError} When the text does not fit in version 40.
 */
export function qrMatrix(text, { errorCorrection = 'M', mask = null, minVersion = 1 } = {}) {
  const ecl = Object.prototype.hasOwnProperty.call(ECL_INDEX, errorCorrection) ? errorCorrection : 'M';
  const bytes = utf8Bytes(text);
  let ver = Math.max(1, Math.min(40, Number(minVersion) || 1));
  for (; ver <= 40; ver++) {
    const countBits = ver <= 9 ? 8 : 16;
    if (4 + countBits + bytes.length * 8 <= dataCodewords(ver, ecl) * 8 && bytes.length < 1 << countBits) break;
  }
  if (ver > 40) throw new RangeError('Text too long for a QR code');
  const countBits = ver <= 9 ? 8 : 16;
  const capacity = dataCodewords(ver, ecl) * 8;
  const bits = [];
  const push = (value, length) => { for (let i = length - 1; i >= 0; i--) bits.push((value >>> i) & 1); };
  push(0b0100, 4);
  push(bytes.length, countBits);
  for (const b of bytes) push(b, 8);
  push(0, Math.min(4, capacity - bits.length));
  push(0, (8 - (bits.length % 8)) % 8);
  for (let pad = 0xec; bits.length < capacity; pad ^= 0xec ^ 0x11) push(pad, 8);
  const data = [];
  for (let i = 0; i < bits.length; i += 8) {
    let b = 0;
    for (let j = 0; j < 8; j++) b = (b << 1) | bits[i + j];
    data.push(b);
  }

  const e = ECL_INDEX[ecl];
  const blockCount = ECC_BLOCKS[e][ver];
  const eccLength = ECC_PER_BLOCK[e][ver];
  const raw = Math.floor(rawDataModules(ver) / 8);
  const shortBlocks = blockCount - (raw % blockCount);
  const shortLength = Math.floor(raw / blockCount);
  const divisor = rsDivisor(eccLength);
  const blocks = [];
  for (let i = 0, k = 0; i < blockCount; i++) {
    const chunk = data.slice(k, k + shortLength - eccLength + (i < shortBlocks ? 0 : 1));
    k += chunk.length;
    const ecc = rsRemainder(chunk, divisor);
    if (i < shortBlocks) chunk.push(0);
    blocks.push(chunk.concat(ecc));
  }
  const codewords = [];
  for (let i = 0; i < blocks[0].length; i++) {
    for (let j = 0; j < blocks.length; j++) {
      if (i !== shortLength - eccLength || j >= shortBlocks) codewords.push(blocks[j][i]);
    }
  }
  const chosen = Number.isInteger(mask) && mask >= 0 && mask <= 7 ? mask : null;
  const result = build(ver, ecl, codewords, chosen);
  return { size: result.size, version: ver, mask: result.mask, errorCorrection: ecl, modules: result.modules };
}

/**
 * Horizontal runs of dark modules per row, `[{ row, col, length }]`, so a renderer can draw a code
 * with one rectangle per run instead of one per module.
 */
export function qrRowRuns(qr) {
  const runs = [];
  const size = qr ? qr.size : 0;
  for (let row = 0; row < size; row++) {
    let col = 0;
    while (col < size) {
      if (!qr.modules[row][col]) { col++; continue; }
      const start = col;
      while (col < size && qr.modules[row][col]) col++;
      runs.push({ row, col: start, length: col - start });
    }
  }
  return runs;
}

/**
 * An SVG path (`d` attribute) drawing the dark modules, offset by a quiet zone of `border`
 * modules; use it with `viewBox="0 0 {size + 2 * border} {size + 2 * border}"`.
 */
export function qrSvgPath(qr, border = 4) {
  return qrRowRuns(qr).map((run) => 'M' + (run.col + border) + ' ' + (run.row + border) + 'h' + run.length + 'v1h-' + run.length + 'z').join('');
}
