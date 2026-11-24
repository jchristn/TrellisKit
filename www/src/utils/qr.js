/**
 * QR code generation for sign-up code join links, using the `qrcode` package (loaded lazily so
 * it only ships in the admin chunk that needs it).
 */
export async function qrPngDataUrl(text, { width = 512 } = {}) {
  const module = await import('qrcode');
  const QRCode = module.default || module;
  return QRCode.toDataURL(text, { width, margin: 2, errorCorrectionLevel: 'M', color: { dark: '#000000', light: '#ffffff' } });
}

/** File name for a code's QR image. */
export function qrFileName(code) {
  return 'join-' + String(code || 'code').toLowerCase() + '.png';
}
