/**
 * Brand images for the website build, generated from a property's brand/ folder with pngjs (pure
 * JavaScript, no native modules). Every file is written at the site root so index.html, the web
 * manifest, and the head tags can reference fixed paths:
 *
 *   /favicon.ico           brand.iconIco as is, or built from icon (16, 32, and 48 px PNG images in one ICO)
 *   /favicon-32.png        icon, 32 x 32
 *   /icon-192.png          icon, 192 x 192 (web manifest)
 *   /icon-512.png          icon, 512 x 512 (web manifest)
 *   /apple-touch-icon.png  icon, 180 x 180 on white (iOS shows transparency as black)
 *   /brand-mark.png        icon, 128 x 128 (header and footer mark)
 *   /logo.png              logo, fit within 512 x 512
 *   /logo-dark.png         the logo for dark backgrounds (<logo name>-dark.png beside the logo), else the logo
 *   /og-image.png          web/og-image.png as is, or the logo centered on white at 1200 x 630
 *   /images/<name>         every image in web/images/ as is (for content pages, for example @cover src)
 */
import { readFileSync } from 'node:fs';
import { PNG } from 'pngjs';

/** Social image size used in head tags. */
export const OG_SIZE = { width: 1200, height: 630 };

function readPng(file) {
  return PNG.sync.read(readFileSync(file));
}

/**
 * Area-average resample (premultiplied alpha) of `src` to `w` x `h`.
 * @returns {{ width: number, height: number, data: Float64Array }} premultiplied RGBA floats
 */
function resample(src, w, h) {
  const out = new Float64Array(w * h * 4);
  const sx = src.width / w;
  const sy = src.height / h;
  for (let y = 0; y < h; y += 1) {
    const y0 = y * sy;
    const y1 = y0 + sy;
    for (let x = 0; x < w; x += 1) {
      const x0 = x * sx;
      const x1 = x0 + sx;
      let r = 0; let g = 0; let b = 0; let a = 0; let area = 0;
      for (let yy = Math.floor(y0); yy < Math.ceil(y1) && yy < src.height; yy += 1) {
        const wy = Math.min(yy + 1, y1) - Math.max(yy, y0);
        for (let xx = Math.floor(x0); xx < Math.ceil(x1) && xx < src.width; xx += 1) {
          const wx = Math.min(xx + 1, x1) - Math.max(xx, x0);
          const weight = wx * wy;
          const i = (yy * src.width + xx) * 4;
          const alpha = src.data[i + 3] / 255;
          r += src.data[i] * alpha * weight;
          g += src.data[i + 1] * alpha * weight;
          b += src.data[i + 2] * alpha * weight;
          a += alpha * weight;
          area += weight;
        }
      }
      const o = (y * w + x) * 4;
      out[o] = r / area; out[o + 1] = g / area; out[o + 2] = b / area; out[o + 3] = a / area;
    }
  }
  return { width: w, height: h, data: out };
}

function parseHex(hex) {
  const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex || '');
  return m ? [parseInt(m[1], 16), parseInt(m[2], 16), parseInt(m[3], 16)] : null;
}

/**
 * Draw `src` scaled to fit within `box` x `box` (aspect kept), centered on a canvas of
 * `width` x `height` with an optional solid background color.
 * @returns {Buffer} PNG bytes
 */
export function fitPng(src, width, height, { box = Math.min(width, height), background = null } = {}) {
  const scale = Math.min(box / src.width, box / src.height);
  const w = Math.max(1, Math.round(src.width * scale));
  const h = Math.max(1, Math.round(src.height * scale));
  const scaled = resample(src, w, h);
  const ox = Math.floor((width - w) / 2);
  const oy = Math.floor((height - h) / 2);
  const bg = parseHex(background);
  const png = new PNG({ width, height });
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const o = (y * width + x) * 4;
      let r = 0; let g = 0; let b = 0; let a = 0;
      const ix = x - ox;
      const iy = y - oy;
      if (ix >= 0 && iy >= 0 && ix < w && iy < h) {
        const i = (iy * w + ix) * 4;
        r = scaled.data[i]; g = scaled.data[i + 1]; b = scaled.data[i + 2]; a = scaled.data[i + 3];
      }
      if (bg) {
        r += bg[0] * (1 - a); g += bg[1] * (1 - a); b += bg[2] * (1 - a); a = 1;
        png.data[o] = Math.round(r); png.data[o + 1] = Math.round(g); png.data[o + 2] = Math.round(b);
      } else if (a > 0) {
        png.data[o] = Math.round(r / a); png.data[o + 1] = Math.round(g / a); png.data[o + 2] = Math.round(b / a);
      }
      png.data[o + 3] = Math.round(a * 255);
    }
  }
  return PNG.sync.write(png);
}

/** Sizes in a generated favicon.ico (tabs use 16 and 32; search results want 48 or larger). */
export const ICO_SIZES = [16, 32, 48];

/**
 * An ICO file holding PNG images (supported by every current browser), one per size.
 * @param {PNG} src source image
 * @param {number[]} [sizes] square sizes in pixels (1 to 256)
 * @returns {Buffer} ICO bytes
 */
export function buildIco(src, sizes = ICO_SIZES) {
  const images = sizes.map((size) => ({ size, data: fitPng(src, size, size) }));
  const header = Buffer.alloc(6 + 16 * images.length);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(images.length, 4);
  let offset = header.length;
  images.forEach((image, i) => {
    const entry = 6 + 16 * i;
    header.writeUInt8(image.size >= 256 ? 0 : image.size, entry);
    header.writeUInt8(image.size >= 256 ? 0 : image.size, entry + 1);
    header.writeUInt8(0, entry + 2);
    header.writeUInt8(0, entry + 3);
    header.writeUInt16LE(1, entry + 4);
    header.writeUInt16LE(32, entry + 6);
    header.writeUInt32LE(image.data.length, entry + 8);
    header.writeUInt32LE(offset, entry + 12);
    offset += image.data.length;
  });
  return Buffer.concat([header, ...images.map((image) => image.data)]);
}

/**
 * Every brand file for a loaded property (see scripts/property.mjs `loadSite`).
 * @param {{ files: { logo: string, logoDark?: string|null, icon: string, iconIco: string|null, ogImage: string|null, images?: Record<string, string> } }} loaded
 * @returns {Record<string, Buffer>} site-root file name to bytes
 */
export function buildBrandAssets(loaded) {
  const { files } = loaded;
  const icon = readPng(files.icon);
  const logo = readPng(files.logo);
  const assets = {
    'favicon-32.png': fitPng(icon, 32, 32),
    'icon-192.png': fitPng(icon, 192, 192),
    'icon-512.png': fitPng(icon, 512, 512),
    'apple-touch-icon.png': fitPng(icon, 180, 180, { box: 160, background: '#ffffff' }),
    'brand-mark.png': fitPng(icon, 128, 128),
    'logo.png': fitPng(logo, 512, 512),
    // The dark theme shows this one. Without a dark logo it is the same image, so the page never has a broken link.
    'logo-dark.png': fitPng(files.logoDark ? readPng(files.logoDark) : logo, 512, 512),
    'og-image.png': files.ogImage ? readFileSync(files.ogImage) : fitPng(logo, OG_SIZE.width, OG_SIZE.height, { box: 520, background: '#ffffff' })
  };
  assets['favicon.ico'] = files.iconIco ? readFileSync(files.iconIco) : buildIco(icon);
  for (const [name, file] of Object.entries(files.images || {})) assets['images/' + name] = readFileSync(file);
  return assets;
}
