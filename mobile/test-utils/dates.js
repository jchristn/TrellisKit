import { fireEvent, screen } from '@testing-library/react-native';

/**
 * Pick a date in a DateField (month, day, and year pickers) by its testID prefix.
 * @param {string} prefix DateField testID, for example 'register-dob'.
 * @param {string|number} month 1 to 12 (leading zeros allowed).
 * @param {string|number} day 1 to 31 (leading zeros allowed).
 * @param {string|number} year Four digits.
 */
export async function pickDate(prefix, month, day, year) {
  for (const [part, value] of [['month', month], ['day', day], ['year', year]]) {
    await fireEvent.press(await screen.findByTestId(prefix + '-' + part));
    await fireEvent.press(await screen.findByTestId(prefix + '-' + part + '-option-' + String(Number(value))));
  }
}
