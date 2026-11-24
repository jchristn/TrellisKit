import { BrandBackgrounds, BrandSchemes } from '@trelliskit/shared';
import './palette.css';

/**
 * A compact light and dark preview of a palette `{ light, dark }`: each scheme shows its page
 * background with a primary button sample, the accent, and a soft accent chip. Decorative; the
 * caller labels it.
 * @param {{ palette: { light: object, dark: object }, label: string, testId?: string }} props
 */
function PaletteSwatches({ palette, label, testId }) {
  return (
    <div className="palette-swatches" role="img" aria-label={label} data-testid={testId}>
      {BrandSchemes.map((scheme) => {
        const set = (palette && palette[scheme]) || {};
        return (
          <div key={scheme} className="palette-swatch" style={{ background: BrandBackgrounds[scheme] }} data-scheme={scheme}>
            <span className="palette-swatch-button" style={{ background: set.primary, color: set.onPrimary }}>Aa</span>
            <span className="palette-swatch-dot" style={{ background: set.accent }} />
            <span className="palette-swatch-chip" style={{ background: set.accentSoft, color: set.onAccentSoft }}>Aa</span>
          </div>
        );
      })}
    </div>
  );
}

export default PaletteSwatches;
