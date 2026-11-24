/** The explicit iOS save-password offer: only on iOS with the native module and a web domain, and it never throws. */
jest.mock('../modules/shared-credentials', () => ({ __esModule: true, default: { save: jest.fn() } }));

const { Platform } = require('react-native');
const SharedCredentials = require('../modules/shared-credentials').default;
const { offerToSavePassword } = require('../src/native/sharedCredentials');

const property = { domains: { web: 'example.test' } };

afterEach(() => {
  SharedCredentials.save.mockReset();
  Platform.OS = 'ios';
});

test('asks iOS to save the login for the property web domain', async () => {
  Platform.OS = 'ios';
  SharedCredentials.save.mockResolvedValue(true);
  await expect(offerToSavePassword(property, 'pat@example.test', 'secret-pass')).resolves.toBe(true);
  expect(SharedCredentials.save).toHaveBeenCalledWith('example.test', 'pat@example.test', 'secret-pass');
});

test('does nothing on Android or without a web domain', async () => {
  Platform.OS = 'android';
  await expect(offerToSavePassword(property, 'pat@example.test', 'x')).resolves.toBe(false);
  Platform.OS = 'ios';
  await expect(offerToSavePassword({ domains: {} }, 'pat@example.test', 'x')).resolves.toBe(false);
  expect(SharedCredentials.save).not.toHaveBeenCalled();
});

test('a native failure is swallowed', async () => {
  Platform.OS = 'ios';
  SharedCredentials.save.mockRejectedValue(new Error('denied'));
  await expect(offerToSavePassword(property, 'pat@example.test', 'x')).resolves.toBe(false);
});
