#!/usr/bin/env node
/**
 * Generate a property's app icons and splash images from its brand folder.
 *
 * Usage (from mobile/):
 *   node scripts/generate-property-assets.js [propertyDir]
 *   npm run assets -- [propertyDir]
 *
 * propertyDir is absolute or relative to the current directory. When omitted, TK_PROPERTY_DIR
 * (absolute or relative to the engine root, the folder that contains mobile/) is used, then the
 * neutral sample (samples/example).
 *
 * Input:  <propertyDir>/property.json and the logo it names (brand.logo, for example
 *         brand/logo.png). An optional brand/logo-dark.png is used for the dark splash.
 * Output: <propertyDir>/mobile/
 *   icon.png                      1024x1024, opaque (iOS and store icon)
 *   android-icon-foreground.png   1024x1024, transparent, logo inside the adaptive icon safe zone
 *   android-icon-background.png   1024x1024, solid background color
 *   android-icon-monochrome.png   1024x1024, white silhouette (themed icons, notification icon)
 *   splash-icon.png               1024x1024, transparent logo for the light splash
 *   splash-icon-dark.png          1024x1024, transparent logo for the dark splash
 *   favicon.png                   48x48 (web)
 *   logo.png                      512x512, transparent (shown in the app, for example on Welcome)
 *
 * Images are produced with jimp-compact (pure JavaScript, pinned in devDependencies), so no
 * native toolchain is needed. Re-run after changing the property's logo, and commit the output
 * with the property.
 */
const fs = require('fs');
const path = require('path');
const Jimp = require('jimp-compact');
const { readProperty, resolvePropertyDir } = require('./property');

/** Background behind the logo on the opaque icon and the Android adaptive icon. */
const IconBackground = '#FFFFFF';

/** Share of the canvas the logo may use, per output. */
const Scale = Object.freeze({ icon: 0.8, adaptive: 0.6, splash: 1, logo: 1, favicon: 1 });

async function fitted(source, canvas, scale, background) {
  const size = Math.round(canvas * scale);
  const logo = source.clone().contain(size, size);
  const base = new Jimp(canvas, canvas, background ? Jimp.cssColorToHex(background) : 0x00000000);
  const offset = Math.round((canvas - size) / 2);
  return base.composite(logo, offset, offset);
}

function silhouette(image) {
  const out = image.clone();
  out.scan(0, 0, out.bitmap.width, out.bitmap.height, function paint(_x, _y, idx) {
    this.bitmap.data[idx] = 255;
    this.bitmap.data[idx + 1] = 255;
    this.bitmap.data[idx + 2] = 255;
  });
  return out;
}

/**
 * Generate every mobile asset for one property.
 * @param {string} propertyDir Absolute property directory.
 * @returns {Promise<string[]>} Written file paths.
 */
async function generate(propertyDir) {
  const property = readProperty(propertyDir);
  const logoPath = path.join(propertyDir, (property.brand && property.brand.logo) || 'brand/logo.png');
  if (!fs.existsSync(logoPath)) throw new Error('Logo not found: ' + logoPath);
  const darkLogoPath = path.join(path.dirname(logoPath), 'logo-dark.png');
  const logo = await Jimp.read(logoPath);
  const darkLogo = fs.existsSync(darkLogoPath) ? await Jimp.read(darkLogoPath) : logo;
  const outDir = path.join(propertyDir, 'mobile');
  fs.mkdirSync(outDir, { recursive: true });

  const outputs = {
    'icon.png': await fitted(logo, 1024, Scale.icon, IconBackground),
    'android-icon-foreground.png': await fitted(logo, 1024, Scale.adaptive),
    'android-icon-background.png': new Jimp(1024, 1024, Jimp.cssColorToHex(IconBackground)),
    'android-icon-monochrome.png': silhouette(await fitted(logo, 1024, Scale.adaptive)),
    'splash-icon.png': await fitted(logo, 1024, Scale.splash),
    'splash-icon-dark.png': await fitted(darkLogo, 1024, Scale.splash),
    'favicon.png': await fitted(logo, 48, Scale.favicon),
    'logo.png': await fitted(logo, 512, Scale.logo)
  };
  const written = [];
  for (const [name, image] of Object.entries(outputs)) {
    const file = path.join(outDir, name);
    await image.writeAsync(file);
    written.push(file);
  }
  return written;
}

module.exports = { generate };

if (require.main === module) {
  const dir = process.argv[2] ? path.resolve(process.cwd(), process.argv[2]) : resolvePropertyDir();
  generate(dir)
    .then((files) => {
      for (const file of files) process.stdout.write('wrote ' + file + '\n');
    })
    .catch((error) => {
      process.stderr.write(error.message + '\n');
      process.exit(1);
    });
}
