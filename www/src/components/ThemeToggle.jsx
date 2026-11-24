import strings from '../strings.js';
import { useTheme } from '../context/ThemeContext.jsx';
import Icon from './Icon.jsx';

/** Icon button that switches between light and dark themes. */
function ThemeToggle() {
  const { theme, toggleTheme } = useTheme();
  const label = theme === 'dark' ? strings.common.themeToLight : strings.common.themeToDark;
  return (
    <button type="button" className="icon-button" onClick={toggleTheme} aria-label={label} title={label}>
      <Icon name={theme === 'dark' ? 'sun' : 'moon'} size={18} />
    </button>
  );
}

export default ThemeToggle;
