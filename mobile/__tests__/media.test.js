import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as SecureStore from 'expo-secure-store';
import * as ImageManipulator from 'expo-image-manipulator';
import * as ImagePicker from 'expo-image-picker';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { MaxAvatarBytes, prepareAvatarUpload } from '../src/media/avatar';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';

const APP_DIR = path.resolve(__dirname, '../app');

function fakeManipulator(sizes) {
  const saveAsync = jest.fn(async ({ compress }) => ({ uri: 'file:///out-' + compress + '.jpg' }));
  const context = { resize: jest.fn(), renderAsync: jest.fn(async () => ({ saveAsync })) };
  context.resize.mockReturnValue(context);
  const read = jest.fn(async () => ({ size: sizes.shift() }));
  return { manipulator: { SaveFormat: { JPEG: 'jpeg' }, ImageManipulator: { manipulate: jest.fn(() => context) } }, context, saveAsync, read };
}

describe('avatar processing', () => {
  test('resizes large photos to 1024 on the long edge and saves a JPEG', async () => {
    const fake = fakeManipulator([300000]);
    const result = await prepareAvatarUpload({ uri: 'file:///in.heic', width: 4032, height: 3024 }, fake);
    expect(fake.context.resize).toHaveBeenCalledWith({ width: 1024 });
    expect(fake.saveAsync).toHaveBeenCalledWith({ compress: 0.8, format: 'jpeg' });
    expect(result).toEqual({ body: { size: 300000 }, contentType: 'image/jpeg' });
  });

  test('lowers quality until the file is under 5 MB', async () => {
    const fake = fakeManipulator([MaxAvatarBytes + 1, MaxAvatarBytes + 1, 1000]);
    const result = await prepareAvatarUpload({ uri: 'file:///in.jpg', width: 800, height: 1000 }, fake);
    expect(fake.context.resize).not.toHaveBeenCalled();
    expect(fake.saveAsync.mock.calls.map((c) => c[0].compress)).toEqual([0.8, 0.6, 0.4]);
    expect(result.body.size).toBe(1000);
  });

  test('gives up with TooLarge when nothing fits', async () => {
    const fake = fakeManipulator([MaxAvatarBytes + 1, MaxAvatarBytes + 1, MaxAvatarBytes + 1, MaxAvatarBytes + 1]);
    await expect(prepareAvatarUpload({ uri: 'file:///in.jpg', width: 10, height: 10 }, fake)).rejects.toMatchObject({ code: 'TooLarge' });
  });
});

describe('photo upload from the profile form', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('uploads the processed bytes with PUT /me/avatar', async () => {
    ImagePicker.launchImageLibraryAsync.mockResolvedValueOnce({ canceled: false, assets: [{ uri: 'file:///picked.jpg', width: 2000, height: 2000 }] });
    const saveAsync = jest.fn(async () => ({ uri: 'file:///small.jpg' }));
    const context = { resize: jest.fn(), renderAsync: jest.fn(async () => ({ saveAsync })) };
    ImageManipulator.ImageManipulator.manipulate.mockReturnValue(context);
    const blob = { size: 1234, kind: 'jpeg-bytes' };
    const server = installFakeServer({ ...signedInAs(makeUser()), 'PUT /me/avatar': makeUser({ avatarUrl: '/api/v1/media/avatars/usr_1.jpg' }) });
    const realFetch = global.fetch;
    global.fetch = jest.fn((url, init) => (String(url).startsWith('file://') ? Promise.resolve({ blob: async () => blob }) : realFetch(url, init)));

    await renderRouter(APP_DIR, { initialUrl: '/account/edit-profile' });
    await fireEvent.press(await screen.findByTestId('choose-photo'));
    await waitFor(() => expect(server.called('PUT /me/avatar')).toHaveLength(1));
    const call = server.called('PUT /me/avatar')[0];
    expect(call.headers['Content-Type']).toBe('image/jpeg');
    expect(call.body).toBe(blob);
    expect(await screen.findByTestId('remove-photo')).toBeOnTheScreen();
  });
});

describe('sign-out', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('revokes the refresh token on the server and clears secure storage', async () => {
    const server = installFakeServer({ ...signedInAs(makeUser()), 'POST /auth/logout': { status: 204, body: null } });
    const router = await renderRouter(APP_DIR, { initialUrl: '/profile' });
    await fireEvent.press(await screen.findByTestId('profile-sign-out'));
    await waitFor(() => expect(router.getPathname()).toBe('/welcome'));
    expect(server.called('POST /auth/logout')[0].body).toEqual({ refreshToken: 'refresh-2' });
    expect(SecureStore.__store.has('tk.refreshToken')).toBe(false);
    expect(await screen.findByText('You are signed out.')).toBeOnTheScreen();
  });
});
