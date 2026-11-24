/**
 * Jest setup: in-memory and fake implementations of the native modules the app uses, so tests
 * run like a development build with every native module present. Individual tests override
 * behavior through the jest.fn handles these mocks expose.
 */

// Expo config as a build would embed it: app.config.js evaluated for the active property
// (TK_PROPERTY_DIR, default the neutral sample), so extra.property is the bundled property.
jest.mock('expo-constants', () => {
  const actual = jest.requireActual('expo-constants');
  const Constants = actual.default || actual;
  const expoConfig = require('./app.config.js')();
  const mocked = Object.create(Constants);
  Object.defineProperty(mocked, 'expoConfig', { value: expoConfig, enumerable: true });
  return { __esModule: true, ...actual, default: mocked };
});

jest.mock('expo-secure-store', () => {
  const store = new Map();
  return {
    __store: store,
    AFTER_FIRST_UNLOCK: 'AFTER_FIRST_UNLOCK',
    getItemAsync: jest.fn(async (key) => (store.has(key) ? store.get(key) : null)),
    setItemAsync: jest.fn(async (key, value) => {
      store.set(key, value);
    }),
    deleteItemAsync: jest.fn(async (key) => {
      store.delete(key);
    }),
    isAvailableAsync: jest.fn(async () => true)
  };
});

jest.mock('expo-web-browser', () => ({
  openBrowserAsync: jest.fn(async () => ({ type: 'opened' }))
}));

jest.mock('expo-crypto', () => {
  let n = 0;
  return {
    CryptoDigestAlgorithm: { SHA256: 'SHA-256' },
    randomUUID: jest.fn(() => {
      n += 1;
      return 'uuid-' + n;
    }),
    digestStringAsync: jest.fn(async (_algorithm, data) => 'sha256(' + data + ')')
  };
});

jest.mock('expo-apple-authentication', () => {
  const { Pressable, Text } = require('react-native');
  function AppleAuthenticationButton({ onPress, testID }) {
    return (
      <Pressable testID={testID} onPress={onPress} accessibilityRole="button" accessibilityLabel="Continue with Apple">
        <Text>Continue with Apple</Text>
      </Pressable>
    );
  }
  return {
    isAvailableAsync: jest.fn(async () => true),
    signInAsync: jest.fn(),
    AppleAuthenticationButton,
    AppleAuthenticationScope: { FULL_NAME: 0, EMAIL: 1 },
    AppleAuthenticationButtonType: { SIGN_IN: 0, CONTINUE: 1 },
    AppleAuthenticationButtonStyle: { WHITE: 0, WHITE_OUTLINE: 1, BLACK: 2 }
  };
});

jest.mock('@react-native-google-signin/google-signin', () => ({
  GoogleSignin: {
    configure: jest.fn(),
    hasPlayServices: jest.fn(async () => true),
    signIn: jest.fn(),
    signOut: jest.fn(async () => null)
  },
  statusCodes: { SIGN_IN_CANCELLED: 'SIGN_IN_CANCELLED', IN_PROGRESS: 'IN_PROGRESS' }
}));
// The Google loader checks that the native module is linked before requiring the package.
require('react-native').NativeModules.RNGoogleSignin = {};

jest.mock('expo-image-picker', () => ({
  UIImagePickerPreferredAssetRepresentationMode: { Automatic: 'automatic', Compatible: 'compatible', Current: 'current' },
  requestMediaLibraryPermissionsAsync: jest.fn(async () => ({ granted: true })),
  requestCameraPermissionsAsync: jest.fn(async () => ({ granted: true })),
  launchImageLibraryAsync: jest.fn(async () => ({ canceled: true, assets: null })),
  launchCameraAsync: jest.fn(async () => ({ canceled: true, assets: null }))
}));

// FlashList measures its layout natively; its jest setup supplies fixed sizes.
require('@shopify/flash-list/jestSetup');

// expo-image: a plain view that keeps `source` (uri and headers) for assertions.
jest.mock('expo-image', () => {
  const { View } = require('react-native');
  function Image({ source, testID, accessibilityLabel, accessible, onError, style }) {
    return <View testID={testID} source={source} accessibilityLabel={accessibilityLabel} accessible={accessible} onError={onError} style={style} />;
  }
  return { Image };
});

// expo-video: a player object that records its source, and a view that renders nothing native.
// Players are kept per source (like the hook, one player per mount) in __players; tests call
// player.__emit(event, payload) to drive playback events.
jest.mock('expo-video', () => {
  const React = require('react');
  const { View } = require('react-native');
  const players = [];
  function makePlayer(source) {
    const listeners = new Map();
    const player = {
      source,
      currentTime: 0,
      play: jest.fn(),
      pause: jest.fn(),
      replaceAsync: jest.fn(async () => {}),
      addListener: jest.fn((name, fn) => {
        if (!listeners.has(name)) listeners.set(name, new Set());
        listeners.get(name).add(fn);
        return { remove: () => listeners.get(name).delete(fn) };
      }),
      __emit: (name, payload) => Array.from(listeners.get(name) || []).forEach((fn) => fn(payload))
    };
    players.push(player);
    return player;
  }
  return {
    useVideoPlayer: jest.fn((source, setup) => {
      const ref = React.useRef(null);
      if (!ref.current) {
        ref.current = makePlayer(source);
        if (typeof setup === 'function') setup(ref.current);
      } else ref.current.source = source;
      return ref.current;
    }),
    VideoView: ({ player, testID }) => <View testID={testID} player={player} />,
    __players: players
  };
});

jest.mock('expo-keep-awake', () => ({
  activateKeepAwakeAsync: jest.fn(async () => {}),
  deactivateKeepAwake: jest.fn(async () => {})
}));

jest.mock('expo-haptics', () => ({
  selectionAsync: jest.fn(async () => {}),
  impactAsync: jest.fn(async () => {}),
  ImpactFeedbackStyle: { Light: 'light', Medium: 'medium', Heavy: 'heavy' }
}));

// expo-file-system: an in-memory file table (uri to size). Tests set files with __setFile and
// read the recorded uploads from __uploads and downloads from __downloads. Files have no text
// API here (the property cache sees "no file system"); the library's private storage is mocked
// below instead.
jest.mock('expo-file-system', () => {
  const files = new Map();
  const uploads = [];
  const downloads = [];
  const join = (parts) =>
    parts
      .map((p) => (p && typeof p === 'object' ? p.uri : String(p)))
      .reduce((acc, part) => (acc ? acc.replace(/\/+$/, '') + '/' + String(part).replace(/^\/+/, '') : part), '');
  class Directory {
    constructor(...parts) {
      this.uri = join(parts).replace(/\/?$/, '/');
    }
    get exists() {
      return true;
    }
    create() {}
  }
  class File {
    constructor(...parts) {
      this.uri = parts.length === 1 && typeof parts[0] === 'string' ? parts[0] : join(parts);
    }
    get size() {
      return files.has(this.uri) ? files.get(this.uri) : null;
    }
    get exists() {
      return files.has(this.uri);
    }
    delete() {
      files.delete(this.uri);
    }
    async preview() {}
    static async downloadFileAsync(url, destination, options = {}) {
      const file = destination instanceof File ? destination : new File(destination.uri, 'download');
      downloads.push({ url, uri: file.uri, options });
      files.set(file.uri, 1000);
      if (options.onProgress) options.onProgress({ bytesWritten: 1000, totalBytes: 1000 });
      return file;
    }
    open() {
      const uri = this.uri;
      return {
        offset: 0,
        async readBytes(length) {
          const size = files.get(uri) || 0;
          const n = Math.max(0, Math.min(length, size - this.offset));
          this.offset += n;
          return new Uint8Array(n);
        },
        close() {}
      };
    }
    async upload(url, options = {}) {
      uploads.push({ uri: this.uri, url, options });
      const size = files.get(this.uri) || 0;
      if (options.onProgress) {
        options.onProgress({ bytesSent: Math.floor(size / 2), totalBytes: size });
        options.onProgress({ bytesSent: size, totalBytes: size });
      }
      return { status: 200, body: '', headers: { ETag: '"single-etag"' } };
    }
  }
  return {
    File,
    Directory,
    UploadType: { BINARY_CONTENT: 0, MULTIPART: 1 },
    Paths: { cache: { uri: 'file:///cache/' }, document: { uri: 'file:///document/' } },
    __setFile: (uri, size) => files.set(uri, size),
    __files: files,
    __uploads: uploads,
    __downloads: downloads,
    __reset: () => {
      files.clear();
      uploads.length = 0;
      downloads.length = 0;
    }
  };
});

jest.mock('expo-image-manipulator', () => ({
  SaveFormat: { JPEG: 'jpeg', PNG: 'png' },
  ImageManipulator: { manipulate: jest.fn() }
}));

// expo-iap is reached only through src/native/iap; the default is "no store in this build".
jest.mock('./src/native/iap', () => ({ getIap: jest.fn(() => null) }));

// expo-notifications: permission undetermined until requested; requests are granted by default.
jest.mock('expo-notifications', () => ({
  AndroidImportance: { DEFAULT: 3, HIGH: 4 },
  IosAuthorizationStatus: { NOT_DETERMINED: 0, DENIED: 1, AUTHORIZED: 2, PROVISIONAL: 3 },
  getPermissionsAsync: jest.fn(async () => ({ granted: false, status: 'undetermined', canAskAgain: true })),
  requestPermissionsAsync: jest.fn(async () => ({ granted: true, status: 'granted', canAskAgain: true })),
  getExpoPushTokenAsync: jest.fn(async () => ({ type: 'expo', data: 'ExponentPushToken[test-device]' })),
  setNotificationChannelAsync: jest.fn(async () => null),
  setNotificationHandler: jest.fn(),
  addPushTokenListener: jest.fn(() => ({ remove: jest.fn() })),
  addNotificationReceivedListener: jest.fn(() => ({ remove: jest.fn() })),
  addNotificationResponseReceivedListener: jest.fn(() => ({ remove: jest.fn() })),
  getLastNotificationResponseAsync: jest.fn(async () => null),
  setBadgeCountAsync: jest.fn(async () => true)
}));

jest.mock('expo-device', () => ({ isDevice: true, modelName: 'iPhone 16', deviceName: 'Test Device' }));

// expo-network: online by default. Tests call __setState to go offline or back online.
jest.mock('expo-network', () => {
  const listeners = new Set();
  const online = { type: 'WIFI', isConnected: true, isInternetReachable: true };
  let state = { ...online };
  return {
    NetworkStateType: { NONE: 'NONE', UNKNOWN: 'UNKNOWN', WIFI: 'WIFI', CELLULAR: 'CELLULAR' },
    getNetworkStateAsync: jest.fn(async () => state),
    addNetworkStateListener: jest.fn((fn) => {
      listeners.add(fn);
      return { remove: () => listeners.delete(fn) };
    }),
    __setState: (next) => {
      state = { ...state, ...next };
      listeners.forEach((fn) => fn(state));
    },
    __reset: () => {
      state = { ...online };
    }
  };
});

// expo-updates: disabled (as in development) unless a test enables it. Tests call __set to
// change isEnabled, updateId, and the like, and __reset to restore the defaults.
jest.mock('expo-updates', () => {
  const defaults = { isEnabled: false, updateId: null, channel: null, runtimeVersion: null, isEmbeddedLaunch: true };
  const mod = {
    ...defaults,
    checkForUpdateAsync: jest.fn(async () => ({ isAvailable: false, isRollBackToEmbedded: false, manifest: undefined })),
    fetchUpdateAsync: jest.fn(async () => ({ isNew: false, isRollBackToEmbedded: false, manifest: undefined })),
    reloadAsync: jest.fn(async () => {}),
    setUpdateRequestHeadersOverride: jest.fn(),
    __set: (values) => Object.assign(mod, values),
    __reset: () => Object.assign(mod, defaults)
  };
  return mod;
});

// expo-location: foreground permission granted and a fixed position unless a test overrides it.
jest.mock('expo-location', () => ({
  Accuracy: { Balanced: 3 },
  getForegroundPermissionsAsync: jest.fn(async () => ({ granted: false, status: 'undetermined' })),
  requestForegroundPermissionsAsync: jest.fn(async () => ({ granted: true, status: 'granted' })),
  getCurrentPositionAsync: jest.fn(async () => ({ coords: { latitude: 37.774929, longitude: -122.419416 } }))
}));

// expo-calendar: permission granted, one writable calendar that records created events.
jest.mock('expo-calendar', () => {
  const created = [];
  const updated = [];
  const calendar = {
    id: 'cal_1',
    allowsModifications: true,
    isPrimary: true,
    createEvent: jest.fn(async (details) => {
      const event = { id: 'dev_evt_' + (created.length + 1), ...details };
      created.push(event);
      return event;
    })
  };
  return {
    EntityTypes: { EVENT: 'event' },
    requestCalendarPermissions: jest.fn(async () => ({ granted: true, status: 'granted' })),
    getDefaultCalendarSync: jest.fn(() => calendar),
    getCalendars: jest.fn(async () => [calendar]),
    ExpoCalendarEvent: {
      get: jest.fn(async (id) => {
        const found = created.find((e) => e.id === id);
        return found ? { ...found, update: jest.fn(async (details) => updated.push({ id, details })) } : null;
      })
    },
    __created: created,
    __updated: updated,
    __reset: () => {
      created.length = 0;
      updated.length = 0;
    }
  };
});

// Map modules: plain views that render their children (markers) so tests can press them.
jest.mock('@maplibre/maplibre-react-native', () => {
  const React = require('react');
  const { View } = require('react-native');
  const Map = ({ children, testID, onRegionDidChange, onDidFailLoadingMap, onLongPress }) => (
    <View testID={testID} onRegionDidChange={onRegionDidChange} onDidFailLoadingMap={onDidFailLoadingMap} onLongPress={onLongPress}>
      {children}
    </View>
  );
  const Camera = React.forwardRef(function Camera(_props, ref) {
    React.useImperativeHandle(ref, () => ({ fitBounds: jest.fn(), flyTo: jest.fn(), easeTo: jest.fn(), jumpTo: jest.fn() }));
    return null;
  });
  const Marker = ({ children }) => <View>{children}</View>;
  return { Map, Camera, Marker };
});

jest.mock('react-native-maps', () => {
  const React = require('react');
  const { View } = require('react-native');
  const MapView = React.forwardRef(function MapView({ children, testID, onRegionChangeComplete, onLongPress }, ref) {
    React.useImperativeHandle(ref, () => ({ fitToCoordinates: jest.fn(), animateToRegion: jest.fn() }));
    return (
      <View testID={testID} onRegionChangeComplete={onRegionChangeComplete} onLongPress={onLongPress}>
        {children}
      </View>
    );
  });
  const Marker = ({ children }) => <View>{children}</View>;
  return { __esModule: true, default: MapView, MapView, Marker };
});

// The library's private key-value storage (offline progress queue, download index): in memory.
jest.mock('./src/library/storage', () => {
  const store = new Map();
  return {
    fileStorage: {
      getItem: jest.fn(async (key) => (store.has(key) ? store.get(key) : null)),
      setItem: jest.fn(async (key, value) => {
        store.set(key, String(value));
      }),
      removeItem: jest.fn(async (key) => {
        store.delete(key);
      })
    },
    downloadsFolder: () => ({ uri: 'file:///document/tk-library/downloads/' }),
    __store: store
  };
});

// The realtime client stays off unless a test turns it on with a fake WebSocket
// (src/messaging/realtimeConfig: setRealtimeEnabled, setWebSocketImpl, setRealtimeTimers).
require('./src/messaging/realtimeConfig').setRealtimeEnabled(false);
