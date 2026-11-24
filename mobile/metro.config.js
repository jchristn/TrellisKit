/**
 * Metro configuration.
 *
 * mobile/ is a standalone Expo project (not an npm workspace). The shared package is linked with
 * "file:../shared", which npm installs as a symlink. Metro has to watch ../shared and must resolve
 * any bare imports from shared/ against mobile/node_modules, so there is only one copy of each
 * dependency in the bundle.
 *
 * Property images: app code imports them as `tk-property/<file>` (for example
 * `tk-property/logo.png`), which resolves to `<active property>/mobile/<file>`. The active
 * property is TK_PROPERTY_DIR (see scripts/property.js); restart Metro with --clear after
 * changing it.
 */
const path = require('path');
const { getDefaultConfig } = require('expo/metro-config');
const { resolvePropertyDir } = require('./scripts/property');

const projectRoot = __dirname;
const sharedRoot = path.resolve(projectRoot, '../shared');
const propertyDir = resolvePropertyDir();
const PropertyPrefix = 'tk-property/';

const config = getDefaultConfig(projectRoot);

config.watchFolders = [...(config.watchFolders || []), sharedRoot, propertyDir];
config.resolver.nodeModulesPaths = [path.resolve(projectRoot, 'node_modules')];

const defaultResolve = config.resolver.resolveRequest;
config.resolver.resolveRequest = (context, moduleName, platform) => {
  if (moduleName.startsWith(PropertyPrefix)) {
    const file = path.join(propertyDir, 'mobile', moduleName.slice(PropertyPrefix.length));
    return context.resolveRequest(context, file, platform);
  }
  return defaultResolve ? defaultResolve(context, moduleName, platform) : context.resolveRequest(context, moduleName, platform);
};

module.exports = config;
