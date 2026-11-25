/**
 * A second sample fixture: the active bundled property with five bottom shortcuts, Training among
 * them. Used by the five-shortcut navigation tests.
 */
import { property } from './property';

export const fiveShortcutsProperty = {
  ...property,
  features: { ...property.features, training: true, library: true, messaging: true },
  mobile: { ...property.mobile, navigation: { drawerItems: ['forums', 'events', 'directory', 'announcements', 'ideas', 'notifications', 'support', 'settings', 'about'], shortcuts: ['feed', 'training', 'members', 'messages', 'profile'], accountMenu: [] } }
};
