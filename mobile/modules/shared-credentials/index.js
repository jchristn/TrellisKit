import { requireOptionalNativeModule } from 'expo';

/** The native module on iOS builds that include it, otherwise null (Android, Expo Go, tests). */
export default requireOptionalNativeModule('SharedCredentials');
