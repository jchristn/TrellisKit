import { fireEvent, screen, waitFor } from '@testing-library/react-native';

const SHORTCUT = { places: 'tab-directory', events: 'tab-events' };

/**
 * The control that selects a map section: the in-screen segment, or the bottom shortcut when the
 * active property places both places and events there (the screen then omits its segment).
 * @param {'places'|'events'} section
 * @returns {Promise<string>} The testID of the control.
 */
export async function mapSectionControl(section) {
  let id = null;
  await waitFor(() => {
    id = screen.queryByTestId('map-section-' + section) ? 'map-section-' + section : screen.queryByTestId(SHORTCUT[section]) ? SHORTCUT[section] : null;
    expect(id).not.toBeNull();
  });
  return id;
}

/** Select a map section with whichever control the active property shows. */
export async function showMapSection(section) {
  await fireEvent.press(screen.getByTestId(await mapSectionControl(section)));
}

/**
 * Find a destination row in the drawer, or in the header account menu when the active property
 * places it there. Leaves the menu that holds it open.
 * @param {string} id Destination identifier.
 */
export async function findMenuDestination(id) {
  if (screen.queryByTestId('drawer-close')) await fireEvent.press(screen.getByTestId('drawer-close'));
  if (screen.queryByTestId('account-menu-backdrop')) await fireEvent.press(screen.getByTestId('account-menu-backdrop'));
  await fireEvent.press(await screen.findByTestId('header-menu'));
  await screen.findByTestId('navigation-drawer');
  if (screen.queryByTestId('drawer-' + id)) return screen.getByTestId('drawer-' + id);
  await fireEvent.press(screen.getByTestId('drawer-close'));
  await fireEvent.press(await screen.findByTestId('header-account'));
  return screen.findByTestId('drawer-' + id);
}
