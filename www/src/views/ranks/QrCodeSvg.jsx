/**
 * A QR code drawn as SVG with the shared dependency-free encoder (no image or native module).
 * Props: `value` (text to encode), `size` (CSS pixels, default 240), `label` (accessible name),
 * `border` (quiet zone in modules, default 4).
 */
import { useMemo } from 'react';
import { qrMatrix, qrSvgPath } from '@trelliskit/shared';

export function QrCodeSvg({ value, size = 240, label, border = 4 }) {
  const qr = useMemo(() => {
    try { return value ? qrMatrix(value) : null; } catch { return null; }
  }, [value]);
  if (!qr) return null;
  const extent = qr.size + border * 2;
  return (
    <svg className="rank-qr" width={size} height={size} viewBox={'0 0 ' + extent + ' ' + extent} role="img" aria-label={label} shapeRendering="crispEdges" data-testid="check-in-qr">
      <rect width={extent} height={extent} fill="#ffffff" />
      <path d={qrSvgPath(qr, border)} fill="#000000" />
    </svg>
  );
}

export default QrCodeSvg;
