import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as FileSystem from 'expo-file-system';
import * as ImagePicker from 'expo-image-picker';
import { router as appRouter } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { attachPermissions, canAttachMedia, canAttachVideo, canCreateFeedPost, isMuted, isStaffUser, STAFF_ROLES } from '../src/content/permissions';
import { describeContentError } from '../src/content/errors';
import { toUploadable } from '../src/media/pick';
import { contentTypeOf, describeAsset, partRanges, storageHeaders, UploadJob, UploadStatus, validateFile } from '../src/media/upload';
import strings from '../src/strings';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { enumerationResult, makeMedia, makePost } from '../test-utils/community';

const APP_DIR = path.resolve(__dirname, '../app');
const admin = [{ role: 'SystemAdmin', resourceType: null, resourceId: null }];
const poster = (flags = {}) => makeUser({ flags: { canPostToFeed: true, canUploadVideo: true, ...flags } });

function apiError(status, code) {
  return Object.assign(new Error(code), { status, code, body: { error: code } });
}

/** A storage transport that records every PUT. */
function fakeTransport({ failParts = [], failOnce = false } = {}) {
  const puts = [];
  let failedOnce = false;
  return {
    puts,
    size: () => null,
    readRange: jest.fn(async (_uri, start, length) => ({ start, length })),
    putFile: jest.fn(async (uri, url, headers, onProgress) => {
      puts.push({ url, headers });
      onProgress(50, 100);
      return { status: 200, etag: '"single"' };
    }),
    putBytes: jest.fn(async (url, bytes) => {
      const part = Number(new URL(url).searchParams.get('partNumber'));
      if (failParts.includes(part) && (!failOnce || !failedOnce)) {
        failedOnce = true;
        throw new TypeError('Network request failed');
      }
      puts.push({ url, part, bytes });
      return { status: 200, etag: '"etag-' + part + '"' };
    })
  };
}

function multipartUpload(id, partCount, partSize, uploadedParts = []) {
  return {
    mode: 'Multipart',
    expiresUtc: '2099-01-01T00:00:00Z',
    single: null,
    multipart: { partSizeBytes: partSize, partCount, parts: Array.from({ length: partCount }, (_, i) => ({ partNumber: i + 1, method: 'PUT', url: 'https://storage.test/in/' + id + '/original?partNumber=' + (i + 1) })) },
    uploadedParts
  };
}

describe('staff roles', () => {
  test('every global staff role counts as staff, members do not', () => {
    expect([...STAFF_ROLES].sort()).toEqual(['Communicator', 'DirectoryModerator', 'EventManager', 'FeedModerator', 'LibraryEditor', 'Moderator', 'SupportAgent', 'SystemAdmin']);
    for (const role of STAFF_ROLES) expect(isStaffUser({ roles: [{ role, resourceType: null, resourceId: '*' }] })).toBe(true);
    expect(isStaffUser({ roles: [] })).toBe(false);
    expect(isStaffUser({ roles: [{ role: 'Member' }] })).toBe(false);
    expect(isStaffUser(null)).toBe(false);
  });
});

describe('posting permissions', () => {
  test('feed posts: SystemAdmin, or canPostToFeed with canPost and not muted', () => {
    expect(canCreateFeedPost(makeUser())).toBe(false);
    expect(canCreateFeedPost(makeUser({ roles: admin }))).toBe(true);
    expect(canCreateFeedPost(poster())).toBe(true);
    expect(canCreateFeedPost(poster({ canPost: false }))).toBe(false);
    expect(canCreateFeedPost(poster({ isMuted: true, mutedUntilUtc: '2099-01-01T00:00:00Z' }))).toBe(false);
    expect(canCreateFeedPost(poster({ isMuted: true, mutedUntilUtc: '2000-01-01T00:00:00Z' }))).toBe(true);
    expect(isMuted(makeUser({ flags: { isMuted: true } }))).toBe(true);
  });

  test('media: canPostMedia for images, plus canUploadVideo for video; viewer flags win', () => {
    expect(canAttachMedia(makeUser())).toBe(true);
    expect(canAttachMedia(makeUser({ flags: { canPostMedia: false } }))).toBe(false);
    expect(canAttachVideo(makeUser({ flags: { canUploadVideo: false } }))).toBe(false);
    expect(canAttachVideo(makeUser({ roles: admin, flags: { canUploadVideo: false, canPostMedia: false } }))).toBe(true);
    expect(attachPermissions(makeUser(), { canAttachMedia: false, canAttachVideo: true })).toEqual({ media: false, video: false });
    expect(attachPermissions(makeUser({ flags: { canPostMedia: false } }), { canAttachMedia: true, canAttachVideo: false })).toEqual({ media: true, video: false });
  });

  describe('screens', () => {
    beforeEach(() => resetEnvironment());
    afterEach(() => delete global.fetch);

    test('members without posting rights see no New post button and cannot open the composer', async () => {
      installFakeServer({ ...signedInAs(makeUser()), 'POST /feed/enumerate': enumerationResult([]) });
      await renderRouter(APP_DIR, { initialUrl: '/feed' });
      await screen.findByTestId('feed-empty');
      expect(screen.queryByTestId('feed-new-post')).toBeNull();
      await act(async () => appRouter.push('/compose'));
      expect(await screen.findByTestId('compose-not-allowed')).toHaveTextContent(strings.composer.notAllowed, { exact: false });
    });

    test('muted posters see when they can post again', async () => {
      installFakeServer(signedInAs(poster({ isMuted: true, mutedUntilUtc: '2099-03-01T12:00:00Z' })));
      await renderRouter(APP_DIR, { initialUrl: '/compose' });
      expect(await screen.findByTestId('compose-not-allowed')).toHaveTextContent(/You cannot post until/);
    });

    test('flagged members get the composer; attach buttons follow canPostMedia and video follows canUploadVideo', async () => {
      installFakeServer({ ...signedInAs(poster({ canUploadVideo: false })), 'POST /feed/enumerate': enumerationResult([]) });
      const router = await renderRouter(APP_DIR, { initialUrl: '/feed' });
      await fireEvent.press(await screen.findByTestId('feed-new-post'));
      await waitFor(() => expect(router.getPathname()).toBe('/compose'));
      await fireEvent.press(await screen.findByTestId('compose-attach-library'));
      await waitFor(() => expect(ImagePicker.launchImageLibraryAsync).toHaveBeenCalled());
      expect(ImagePicker.launchImageLibraryAsync.mock.calls[0][0]).toMatchObject({ mediaTypes: ['images'], allowsMultipleSelection: true, selectionLimit: 10 });
    });

    test('text-only members (canPostMedia false) get no attach buttons', async () => {
      installFakeServer(signedInAs(poster({ canPostMedia: false })));
      await renderRouter(APP_DIR, { initialUrl: '/compose' });
      expect(await screen.findByTestId('compose-body')).toBeOnTheScreen();
      expect(screen.queryByTestId('compose-attach-library')).toBeNull();
      expect(screen.queryByTestId('compose-attach-camera')).toBeNull();
    });
  });
});

describe('upload sessions', () => {
  const api = () => ({ post: jest.fn(), get: jest.fn(), put: jest.fn(), delete: jest.fn(), request: jest.fn() });
  const image = { uri: 'file:///photo.jpg', kind: 'Image', contentType: 'image/jpeg', sizeBytes: 100, fileName: 'photo.jpg' };

  test('single mode: declare, PUT with exactly the listed headers, complete without parts, then Processing', async () => {
    const client = api();
    const media = makeMedia('med_1', { status: 'Uploading' });
    client.post.mockImplementation(async (p, body) => {
      if (p === '/media/uploads') return { media, upload: { mode: 'Single', single: { method: 'PUT', url: 'https://storage.test/in/med_1/original?sig=1', headers: { 'Content-Type': 'image/jpeg' } }, multipart: null, uploadedParts: [] } };
      if (p === '/media/med_1/complete') return { ...media, status: 'Processing', _body: body };
      throw new Error('unexpected ' + p);
    });
    const transport = fakeTransport();
    const states = [];
    const job = new UploadJob({ api: client, file: image, transport, onChange: (s) => states.push(s.status) });
    const result = await job.start();
    expect(client.post.mock.calls[0]).toEqual(['/media/uploads', { kind: 'Image', contentType: 'image/jpeg', sizeBytes: 100, fileName: 'photo.jpg', durationSeconds: undefined }]);
    expect(transport.puts).toEqual([{ url: 'https://storage.test/in/med_1/original?sig=1', headers: { 'Content-Type': 'image/jpeg' } }]);
    expect(client.post.mock.calls[1]).toEqual(['/media/med_1/complete', {}]);
    expect(result.status).toBe(UploadStatus.Processing);
    expect(states).toEqual([UploadStatus.Uploading, UploadStatus.Uploading, UploadStatus.Completing, UploadStatus.Processing]);
  });

  test('multipart: parts in parallel, ETags kept verbatim, complete lists every part in order', async () => {
    const client = api();
    const media = makeMedia('med_v', { kind: 'Video', status: 'Uploading' });
    client.post.mockImplementation(async (p, body) => {
      if (p === '/media/uploads') return { media, upload: multipartUpload('med_v', 3, 40) };
      if (p === '/media/med_v/complete') {
        client.completeBody = body;
        return { ...media, status: 'Processing' };
      }
      throw new Error('unexpected ' + p);
    });
    const transport = fakeTransport();
    const file = { uri: 'file:///clip.mov', kind: 'Video', contentType: 'video/quicktime', sizeBytes: 100, durationSeconds: 12.5 };
    const result = await new UploadJob({ api: client, file, transport }).start();
    expect(client.post.mock.calls[0][1]).toMatchObject({ kind: 'Video', durationSeconds: 12.5 });
    expect(transport.readRange.mock.calls.map((c) => [c[1], c[2]])).toEqual([[0, 40], [40, 40], [80, 20]]);
    expect(client.completeBody).toEqual({ parts: [{ partNumber: 1, etag: '"etag-1"' }, { partNumber: 2, etag: '"etag-2"' }, { partNumber: 3, etag: '"etag-3"' }] });
    expect(result.status).toBe(UploadStatus.Processing);
    expect(result.progress).toBe(1);
  });

  test('an interrupted multipart upload resumes with fresh URLs and sends only the missing parts', async () => {
    const client = api();
    const media = makeMedia('med_v', { kind: 'Video', status: 'Uploading' });
    client.post.mockImplementation(async (p, body) => {
      if (p === '/media/uploads') return { media, upload: multipartUpload('med_v', 3, 40) };
      if (p === '/media/med_v/upload-urls') return { upload: multipartUpload('med_v', 3, 40, [{ partNumber: 1, etag: '"stored-1"', sizeBytes: 40 }]) };
      if (p === '/media/med_v/complete') {
        client.completeBody = body;
        return { ...media, status: 'Processing' };
      }
      throw new Error('unexpected ' + p);
    });
    const transport = fakeTransport({ failParts: [2, 3] });
    const file = { uri: 'file:///clip.mov', kind: 'Video', contentType: 'video/quicktime', sizeBytes: 100, durationSeconds: 3 };
    const job = new UploadJob({ api: client, file, transport });
    expect((await job.start()).status).toBe(UploadStatus.Interrupted);

    transport.putBytes.mockImplementation(async (url) => {
      const part = Number(new URL(url).searchParams.get('partNumber'));
      transport.puts.push({ url, part });
      return { status: 200, etag: '"etag-' + part + '"' };
    });
    transport.puts.length = 0;
    expect((await job.resume()).status).toBe(UploadStatus.Processing);
    expect(transport.puts.map((p) => p.part).sort()).toEqual([2, 3]);
    expect(client.completeBody.parts).toEqual([{ partNumber: 1, etag: '"stored-1"' }, { partNumber: 2, etag: '"etag-2"' }, { partNumber: 3, etag: '"etag-3"' }]);
  });

  test('UploadIncomplete at complete triggers one resume; UploadExpired fails', async () => {
    const client = api();
    const media = makeMedia('med_1', { status: 'Uploading' });
    let completes = 0;
    client.post.mockImplementation(async (p) => {
      if (p === '/media/uploads') return { media, upload: { mode: 'Single', single: { url: 'https://storage.test/x', headers: {} } } };
      if (p === '/media/med_1/upload-urls') return { upload: { mode: 'Single', single: { url: 'https://storage.test/y', headers: {} } } };
      if (p === '/media/med_1/complete') {
        completes += 1;
        if (completes === 1) throw apiError(409, 'UploadIncomplete');
        return { ...media, status: 'Ready' };
      }
      throw new Error(p);
    });
    const job = new UploadJob({ api: client, file: image, transport: fakeTransport() });
    expect((await job.start()).status).toBe(UploadStatus.Ready);
    expect(completes).toBe(2);

    const expired = api();
    expired.post.mockImplementation(async (p) => {
      if (p === '/media/med_1/upload-urls') throw apiError(410, 'UploadExpired');
      throw new Error(p);
    });
    const stuck = new UploadJob({ api: expired, file: image, transport: fakeTransport() });
    stuck.state = { ...stuck.state, status: UploadStatus.Interrupted, media };
    const failed = await stuck.resume();
    expect(failed.status).toBe(UploadStatus.Failed);
    expect(describeContentError(failed.error)).toBe(strings.errors.uploadExpired);
  });

  test('session errors fail the item with the contract message', async () => {
    for (const [status, code, text] of [
      [413, 'TooLarge', strings.errors.mediaTooLarge],
      [403, 'MediaNotAllowed', strings.errors.mediaNotAllowed],
      [403, 'VideoUploadNotAllowed', strings.errors.videoUploadNotAllowed],
      [400, 'VideoTooLong', strings.errors.videoTooLong],
      [403, 'Muted', strings.errors.muted]
    ]) {
      const client = api();
      client.post.mockRejectedValue(apiError(status, code));
      const result = await new UploadJob({ api: client, file: image, transport: fakeTransport() }).start();
      expect({ code, status: result.status, text: describeContentError(result.error) }).toEqual({ code, status: UploadStatus.Failed, text });
    }
  });

  test('assets: content types, HEIC to JPEG, video duration in seconds, and part ranges', async () => {
    expect(contentTypeOf({ uri: 'file:///a.MOV' })).toBe('video/quicktime');
    expect(contentTypeOf({ mimeType: 'image/jpg' })).toBe('image/jpeg');
    expect(describeAsset({ uri: 'file:///v.mp4', type: 'video', mimeType: 'video/mp4', fileSize: 10, duration: 142600 })).toMatchObject({ kind: 'Video', durationSeconds: 142.6, sizeBytes: 10 });
    expect(validateFile({ kind: 'Image', contentType: 'image/heic', sizeBytes: 5 })).toMatchObject({ code: 'UnsupportedMediaType' });
    expect(validateFile({ kind: 'Image', contentType: 'image/gif', sizeBytes: 5 })).toMatchObject({ code: 'UnsupportedMediaType' });
    expect(partRanges(100, 40)).toEqual([{ partNumber: 1, start: 0, length: 40 }, { partNumber: 2, start: 40, length: 40 }, { partNumber: 3, start: 80, length: 20 }]);

    const saveAsync = jest.fn(async () => ({ uri: 'file:///converted.jpg', width: 10, height: 20 }));
    const manipulator = { SaveFormat: { JPEG: 'jpeg' }, ImageManipulator: { manipulate: () => ({ renderAsync: async () => ({ saveAsync }) }) } };
    const converted = await toUploadable({ uri: 'file:///IMG.HEIC', mimeType: 'image/heic', fileName: 'IMG.HEIC', type: 'image' }, manipulator);
    expect(converted).toMatchObject({ uri: 'file:///converted.jpg', mimeType: 'image/jpeg', fileName: 'IMG.jpg' });
    expect(saveAsync).toHaveBeenCalledWith({ compress: 0.9, format: 'jpeg' });
    const jpeg = { uri: 'file:///a.jpg', mimeType: 'image/jpeg' };
    expect(await toUploadable(jpeg, manipulator)).toBe(jpeg);
  });
});

describe('composer upload flow', () => {
  beforeEach(() => {
    resetEnvironment();
    FileSystem.__reset();
  });
  afterEach(() => delete global.fetch);

  test('pick, upload with progress, process until Ready, then publish with the media id', async () => {
    FileSystem.__setFile('file:///photo.jpg', 2048);
    ImagePicker.launchImageLibraryAsync.mockResolvedValueOnce({ canceled: false, assets: [{ uri: 'file:///photo.jpg', type: 'image', mimeType: 'image/jpeg', width: 100, height: 80, fileName: 'photo.jpg' }] });
    const uploading = makeMedia('med_1', { status: 'Uploading' });
    let polls = 0;
    const server = installFakeServer({
      ...signedInAs(poster()),
      'POST /feed/enumerate': enumerationResult([]),
      'POST /media/uploads': { status: 201, body: { media: uploading, upload: { mode: 'Single', expiresUtc: '2099-01-01T00:00:00Z', single: { method: 'PUT', url: 'http://127.0.0.1:8000/api/v1/media/local-uploads/med_1?exp=1&sig=ab', headers: { 'Content-Type': 'image/jpeg' } }, multipart: null, uploadedParts: [] } } },
      'POST /media/med_1/complete': { status: 202, body: { ...uploading, status: 'Processing' } },
      'POST /media/enumerate': () => {
        polls += 1;
        return enumerationResult([{ ...uploading, status: polls > 1 ? 'Ready' : 'Processing' }]);
      },
      'PUT /media/med_1': (call) => makeMedia('med_1', call.body),
      'POST /feed': (call) => ({ status: 201, body: makePost('fpo_new', { body: call.body.body, attachments: [makeMedia('med_1')] }) })
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await fireEvent.press(await screen.findByTestId('feed-new-post'));
    await fireEvent.changeText(await screen.findByTestId('compose-body'), 'Hello **all**');
    await fireEvent.press(screen.getByTestId('compose-attach-library'));
    await waitFor(() => expect(server.called('POST /media/uploads')).toHaveLength(1));
    expect(server.called('POST /media/uploads')[0].body).toEqual({ kind: 'Image', contentType: 'image/jpeg', sizeBytes: 2048, fileName: 'photo.jpg' });
    await waitFor(() => expect(FileSystem.__uploads).toHaveLength(1));
    expect(FileSystem.__uploads[0].options).toMatchObject({ httpMethod: 'PUT', headers: { 'Content-Type': 'image/jpeg' } });
    await waitFor(() => expect(screen.getByTestId('compose-uploads-0-status')).toHaveTextContent('Processing'));

    // Publishing waits for uploads to settle only while they are still transferring.
    await waitFor(() => expect(screen.getByTestId('compose-uploads-0-status')).toHaveTextContent('Ready'), { timeout: 8000 });
    expect(server.called('POST /media/enumerate')[0].body.filters.ids).toEqual(['med_1']);

    await fireEvent.changeText(screen.getByTestId('compose-uploads-0-alt'), 'A sunny day');
    await fireEvent.press(screen.getByTestId('compose-submit'));
    await waitFor(() => expect(server.called('POST /feed')).toHaveLength(1));
    expect(server.called('PUT /media/med_1').map((c) => c.body)).toEqual([{ altText: 'A sunny day' }]);
    expect(server.called('POST /feed')[0].body).toMatchObject({ body: 'Hello **all**', mediaIds: ['med_1'], commentsEnabled: true });
    await waitFor(() => expect(router.getPathname()).toBe('/feed'));
    expect(await screen.findByTestId('post-fpo_new')).toBeOnTheScreen();
  });

  test('an upload that fails processing shows the reason and blocks publishing', async () => {
    FileSystem.__setFile('file:///big.jpg', 4096);
    ImagePicker.launchImageLibraryAsync.mockResolvedValueOnce({ canceled: false, assets: [{ uri: 'file:///big.jpg', type: 'image', mimeType: 'image/jpeg', fileName: 'big.jpg' }] });
    const uploading = makeMedia('med_2', { status: 'Uploading' });
    const server = installFakeServer({
      ...signedInAs(poster()),
      'POST /media/uploads': { status: 201, body: { media: uploading, upload: { mode: 'Single', single: { url: 'http://127.0.0.1:8000/api/v1/media/local-uploads/med_2', headers: {} } } } },
      'POST /media/med_2/complete': { status: 415, body: { error: 'UnsupportedMediaType', message: 'Nope.' } }
    });
    await renderRouter(APP_DIR, { initialUrl: '/compose' });
    await fireEvent.changeText(await screen.findByTestId('compose-body'), 'Text');
    await fireEvent.press(screen.getByTestId('compose-attach-library'));
    await waitFor(() => expect(screen.getByTestId('compose-uploads-0-status')).toHaveTextContent(strings.errors.unsupportedMediaType));
    await fireEvent.press(screen.getByTestId('compose-submit'));
    expect(screen.getByTestId('compose-error')).toHaveTextContent(strings.composer.waitForUploads);
    expect(server.called('POST /feed')).toHaveLength(0);

    // Once the failed upload is removed the notice is stale and clears without another submit.
    await fireEvent.press(screen.getByTestId('compose-uploads-0-remove'));
    await waitFor(() => expect(screen.queryByText(strings.composer.waitForUploads)).toBeNull());
  });

  test('server field errors from the word filter show on the body', async () => {
    installFakeServer({
      ...signedInAs(poster()),
      'POST /feed': { status: 400, body: { error: 'ContentFiltered', message: 'Filtered.', fieldErrors: { body: 'This contains a word that is not allowed here.' } } }
    });
    await renderRouter(APP_DIR, { initialUrl: '/compose' });
    await fireEvent.changeText(await screen.findByTestId('compose-body'), 'something');
    await fireEvent.press(screen.getByTestId('compose-submit'));
    expect(await screen.findByTestId('compose-body-error')).toHaveTextContent('This contains a word that is not allowed here.');
    expect(screen.getByTestId('compose-error')).toHaveTextContent(strings.errors.contentFiltered);
  });

  test('editing loads the post, keeps its attachments, and saves with PUT', async () => {
    const post = makePost('fpo_1', { author: { id: 'usr_1', displayName: 'Test Member' }, body: 'Old', attachments: [makeMedia('med_9')], viewer: { canEdit: true, canDelete: true, canReact: true, canComment: true, canReport: false, canAttachMedia: true, canAttachVideo: true } });
    const server = installFakeServer({ ...signedInAs(poster()), 'GET /feed/fpo_1': post, 'PUT /feed/fpo_1': (call) => ({ ...post, body: call.body.body, editedUtc: '2026-11-11T00:00:00Z' }) });
    await renderRouter(APP_DIR, { initialUrl: '/compose?id=fpo_1' });
    const body = await screen.findByTestId('compose-body');
    expect(body.props.value).toBe('Old');
    expect(screen.getByTestId('compose-uploads-0-status')).toHaveTextContent('Ready');
    await fireEvent.changeText(body, 'New');
    await fireEvent.press(screen.getByTestId('compose-submit'));
    await waitFor(() => expect(server.called('PUT /feed/fpo_1')).toHaveLength(1));
    expect(server.called('PUT /feed/fpo_1')[0].body).toMatchObject({ body: 'New', mediaIds: ['med_9'] });
  });

  test('mention autocomplete searches members after @ and one character and inserts the handle', async () => {
    const server = installFakeServer({
      ...signedInAs(poster()),
      'POST /members/enumerate': enumerationResult([{ id: 'usr_2', displayName: 'Alex Chen', handle: 'alexchen', avatarUrl: null }])
    });
    await renderRouter(APP_DIR, { initialUrl: '/compose' });
    const body = await screen.findByTestId('compose-body');
    await fireEvent.changeText(body, 'Thanks @a');
    await fireEvent(body, 'selectionChange', { nativeEvent: { selection: { start: 9, end: 9 } } });
    await fireEvent.press(await screen.findByTestId('compose-body-suggestion-alexchen'));
    expect(server.called('POST /members/enumerate')[0].body).toEqual({ maxResults: 8, search: 'a' });
    expect(screen.getByTestId('compose-body').props.value).toBe('Thanks @alexchen ');
  });
});

describe('local upload PUTs carry the property and app headers', () => {
  const identity = { 'X-TK-Property': 'app_test_property', 'X-TK-App': 'test.example.app' };
  const client = () => ({ post: jest.fn(), get: jest.fn(), identityHeaders: () => identity });

  test('storageHeaders adds identity only for the API local upload route; listed headers win', () => {
    expect(storageHeaders(client(), 'https://storage.test/in/med_1/original?sig=1', { 'Content-Type': 'image/jpeg' })).toEqual({ 'Content-Type': 'image/jpeg' });
    expect(storageHeaders(client(), 'http://10.0.2.2:8000/api/v1/media/local-uploads/med_1?sig=1', { 'Content-Type': 'image/jpeg' })).toEqual({ ...identity, 'Content-Type': 'image/jpeg' });
    expect(storageHeaders({}, '/api/v1/media/local-uploads/med_1/parts/2?sig=1')).toEqual({});
  });

  test('single and multipart local uploads send X-TK-Property and X-TK-App', async () => {
    const api = client();
    const media = makeMedia('med_1', { status: 'Uploading' });
    api.post.mockImplementation(async (p) => {
      if (p === '/media/uploads') return { media, upload: { mode: 'Single', single: { method: 'PUT', url: 'http://api.test/api/v1/media/local-uploads/med_1?sig=1', headers: { 'Content-Type': 'image/jpeg' } } } };
      if (p === '/media/med_1/complete') return { ...media, status: 'Processing' };
      throw new Error('unexpected ' + p);
    });
    const transport = fakeTransport();
    await new UploadJob({ api, file: { uri: 'file:///a.jpg', kind: 'Image', contentType: 'image/jpeg', sizeBytes: 10 }, transport }).start();
    expect(transport.puts[0].headers).toEqual({ ...identity, 'Content-Type': 'image/jpeg' });

    const video = makeMedia('med_v', { kind: 'Video', status: 'Uploading' });
    const parts = { mode: 'Multipart', multipart: { partSizeBytes: 50, partCount: 2, parts: [1, 2].map((n) => ({ partNumber: n, url: 'http://api.test/api/v1/media/local-uploads/med_v/parts/' + n + '?partNumber=' + n })) }, uploadedParts: [] };
    api.post.mockImplementation(async (p) => {
      if (p === '/media/uploads') return { media: video, upload: parts };
      if (p === '/media/med_v/complete') return { ...video, status: 'Processing' };
      throw new Error('unexpected ' + p);
    });
    const multi = fakeTransport();
    await new UploadJob({ api, file: { uri: 'file:///v.mov', kind: 'Video', contentType: 'video/quicktime', sizeBytes: 100, durationSeconds: 2 }, transport: multi }).start();
    expect(multi.putBytes.mock.calls.map((c) => c[2])).toEqual([identity, identity]);
  });
});
