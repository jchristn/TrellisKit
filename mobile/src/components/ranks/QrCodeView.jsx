import { qrMatrix, qrRowRuns } from '@trelliskit/shared';
import { useMemo } from 'react';
import { View } from 'react-native';

/**
 * A QR code drawn with plain views from the shared dependency-free encoder, so it needs no native
 * module and ships in an over-the-air update. Dark modules are merged into one view per run.
 * Props: `value` (text to encode), `size` (points, default 240), `label` (accessibility label),
 * `border` (quiet zone in modules, default 4), `testID`.
 */
export default function QrCodeView({ value, size = 240, label, border = 4, testID = 'check-in-qr' }) {
  const qr = useMemo(() => {
    try { return value ? qrMatrix(value) : null; } catch { return null; }
  }, [value]);
  const runs = useMemo(() => (qr ? qrRowRuns(qr) : []), [qr]);
  if (!qr) return null;
  const extent = qr.size + border * 2;
  const unit = size / extent;
  return (
    <View accessible accessibilityRole="image" accessibilityLabel={label} testID={testID} style={{ width: size, height: size, backgroundColor: '#ffffff' }}>
      {runs.map((run) => (
        <View key={run.row + ':' + run.col} style={{ position: 'absolute', left: (run.col + border) * unit, top: (run.row + border) * unit, width: run.length * unit + 0.5, height: unit + 0.5, backgroundColor: '#000000' }} />
      ))}
    </View>
  );
}
