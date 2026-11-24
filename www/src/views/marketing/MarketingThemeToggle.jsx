import strings from '../../strings.js';
import { useTheme } from '../../context/ThemeContext.jsx';
import Icon from '../../components/Icon.jsx';
import useHydrated from './useHydrated.js';

/**
 * Theme toggle for the prerendered marketing shell. Until hydration finishes it renders the light
 * theme state that the static HTML was built with, so the markup matches; it then shows the saved
 * or system theme. The page colors themselves never flash: index.html sets data-theme before paint.
 */
function MarketingThemeToggle() {
  const { theme, toggleTheme } = useTheme();
  const hydrated = useHydrated();
  const shown = hydrated ? theme : 'light';
  const label = shown === 'dark' ? strings.common.themeToLight : strings.common.themeToDark;
  return (
    <button type="button" className="icon-button" onClick={toggleTheme} aria-label={label} title={label}>
      <Icon name={shown === 'dark' ? 'sun' : 'moon'} size={18} />
    </button>
  );
}

export default MarketingThemeToggle;
