namespace TrellisKit.Server.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The per-property directory settings (docs/PHASE4_CONTRACT.md sections 3.5, 3.6, 3.11, and 12): public listings,
    /// the geocoder, and the map. Secrets (directory.geocoder.apiKey, directory.map.proxyKey) are stored encrypted.
    /// </summary>
    public static class DirectoryPropertySettings
    {
        #region Public-Members

        /// <summary>
        /// Prefix of every directory key.
        /// </summary>
        public const string Prefix = "directory.";

        /// <summary>
        /// Public venue routes on or off (default false).
        /// </summary>
        public const string PublicListingsEnabled = "directory.publicListingsEnabled";

        /// <summary>
        /// Phone on public routes (default false).
        /// </summary>
        public const string PublicShowsContact = "directory.publicShowsContact";

        /// <summary>
        /// Venue reviews on or off for the property (default true).
        /// </summary>
        public const string ReviewsEnabled = "directory.reviewsEnabled";

        /// <summary>
        /// Member photos and videos on venues on or off for the property (default true).
        /// </summary>
        public const string MemberMediaEnabled = "directory.memberMediaEnabled";

        /// <summary>
        /// Geocoder provider.
        /// </summary>
        public const string GeocoderProvider = "directory.geocoder.provider";

        /// <summary>
        /// Geocoder base URL (Nominatim and Http).
        /// </summary>
        public const string GeocoderBaseUrl = "directory.geocoder.baseUrl";

        /// <summary>
        /// Geocoder key (secret).
        /// </summary>
        public const string GeocoderApiKey = "directory.geocoder.apiKey";

        /// <summary>
        /// Header that carries the Http geocoder key.
        /// </summary>
        public const string GeocoderAuthHeaderName = "directory.geocoder.authHeaderName";

        /// <summary>
        /// Nominatim contact email parameter.
        /// </summary>
        public const string GeocoderEmail = "directory.geocoder.email";

        /// <summary>
        /// Amazon Location Service place index name.
        /// </summary>
        public const string GeocoderPlaceIndexName = "directory.geocoder.placeIndexName";

        /// <summary>
        /// Amazon Location Service region.
        /// </summary>
        public const string GeocoderRegion = "directory.geocoder.region";

        /// <summary>
        /// AWS access key id for Amazon Location Service, or empty to use the server's own AWS credentials. The
        /// matching secret access key is stored in directory.geocoder.apiKey.
        /// </summary>
        public const string GeocoderAccessKeyId = "directory.geocoder.accessKeyId";

        /// <summary>
        /// Nominatim requests per second.
        /// </summary>
        public const string GeocoderRequestsPerSecond = "directory.geocoder.requestsPerSecond";

        /// <summary>
        /// Geocoder cache lifetime in days.
        /// </summary>
        public const string GeocoderCacheDays = "directory.geocoder.cacheDays";

        /// <summary>
        /// Map provider.
        /// </summary>
        public const string MapProvider = "directory.map.provider";

        /// <summary>
        /// MapLibre style URL.
        /// </summary>
        public const string MapStyleUrl = "directory.map.styleUrl";

        /// <summary>
        /// MapLibre dark style URL.
        /// </summary>
        public const string MapDarkStyleUrl = "directory.map.darkStyleUrl";

        /// <summary>
        /// Client-visible tile key.
        /// </summary>
        public const string MapPublicKey = "directory.map.publicKey";

        /// <summary>
        /// Attribution text.
        /// </summary>
        public const string MapAttribution = "directory.map.attribution";

        /// <summary>
        /// Tile proxy on or off.
        /// </summary>
        public const string MapTileProxy = "directory.map.tileProxy";

        /// <summary>
        /// Secret key the tile proxy adds (secret).
        /// </summary>
        public const string MapProxyKey = "directory.map.proxyKey";

        /// <summary>
        /// Query parameter that carries the proxy key.
        /// </summary>
        public const string MapProxyKeyParameter = "directory.map.proxyKeyParameter";

        /// <summary>
        /// The only origin the tile proxy fetches (empty uses the origin of the style URL).
        /// </summary>
        public const string MapTileBaseUrl = "directory.map.tileBaseUrl";

        /// <summary>
        /// Default map center latitude.
        /// </summary>
        public const string MapDefaultCenterLat = "directory.map.defaultCenterLat";

        /// <summary>
        /// Default map center longitude.
        /// </summary>
        public const string MapDefaultCenterLng = "directory.map.defaultCenterLng";

        /// <summary>
        /// Default zoom.
        /// </summary>
        public const string MapDefaultZoom = "directory.map.defaultZoom";

        /// <summary>
        /// Maximum zoom.
        /// </summary>
        public const string MapMaxZoom = "directory.map.maxZoom";

        /// <summary>
        /// Most map features per enumeration.
        /// </summary>
        public const string MapMaxFeatures = "directory.map.maxFeatures";

        #endregion

        #region Public-Methods

        /// <summary>
        /// The definitions, in display order.
        /// </summary>
        /// <returns>Definitions.</returns>
        public static List<PropertySettingDefinition> Definitions()
        {
            const string listings = "Directory";
            const string geocoding = "Directory geocoding";
            const string map = "Directory map";
            return new List<PropertySettingDefinition>
            {
                new PropertySettingDefinition(PublicListingsEnabled, listings, "Public venue listings", "Serve Public, Active venues on the anonymous /api/v1/public/venues routes in every access model.", SettingTypeEnum.Boolean, "false"),
                new PropertySettingDefinition(PublicShowsContact, listings, "Phone on public listings", "Include the phone number on the public venue routes (email is never public).", SettingTypeEnum.Boolean, "false"),
                new PropertySettingDefinition(ReviewsEnabled, listings, "Venue reviews", "Members rate venues with 1 to 5 stars and a written review; linked instructors may reply.", SettingTypeEnum.Boolean, "true"),
                new PropertySettingDefinition(MemberMediaEnabled, listings, "Member photos and videos", "Members add photos and videos to a venue's Media section.", SettingTypeEnum.Boolean, "true"),
                new PropertySettingDefinition(GeocoderProvider, geocoding, "Geocoder", "None (default: staff place pins), Nominatim, AwsLocation (Amazon Location Service Places), or Http (Pelias or Photon style).",
                    SettingTypeEnum.Enum, "None", Enum.GetNames<GeocoderProviderEnum>().ToList()),
                new PropertySettingDefinition(GeocoderBaseUrl, geocoding, "Geocoder base URL", "Nominatim or Http provider endpoint (https; public internet addresses only).", SettingTypeEnum.String, maxLength: 500),
                new PropertySettingDefinition(GeocoderApiKey, geocoding, "Geocoder API key", "Provider key, or the AWS secret access key for Amazon Location Service (stored encrypted).", SettingTypeEnum.Secret),
                new PropertySettingDefinition(GeocoderAuthHeaderName, geocoding, "Geocoder key header", "Http provider header that carries the key; empty sends it as api_key.", SettingTypeEnum.String, maxLength: 100),
                new PropertySettingDefinition(GeocoderEmail, geocoding, "Geocoder contact email", "Nominatim email parameter when the provider's usage policy requires one.", SettingTypeEnum.String, maxLength: 254),
                new PropertySettingDefinition(GeocoderPlaceIndexName, geocoding, "Place index", "Amazon Location Service place index name.", SettingTypeEnum.String, maxLength: 100),
                new PropertySettingDefinition(GeocoderAccessKeyId, geocoding, "AWS access key id", "Amazon Location Service only: the access key id of an IAM user allowed geo:SearchPlaceIndexForText on the place index, with its secret access key in Geocoder API key. Empty uses the server's own AWS credentials (the task role in production).", SettingTypeEnum.String, maxLength: 128),
                new PropertySettingDefinition(GeocoderRegion, geocoding, "Place index region", "AWS region of the place index, for example us-west-2.", SettingTypeEnum.String, maxLength: 40),
                new PropertySettingDefinition(GeocoderRequestsPerSecond, geocoding, "Geocoder requests per second", "Nominatim throttle. Default 1.", SettingTypeEnum.Integer, "1", null, 1, 50),
                new PropertySettingDefinition(GeocoderCacheDays, geocoding, "Geocoder cache days", "How long geocoder answers are reused. Default 90.", SettingTypeEnum.Integer, "90", null, 1, 3650),
                new PropertySettingDefinition(MapProvider, map, "Map", "None (default: list view), MapLibre, or PlatformNative.", SettingTypeEnum.Enum, "None", Enum.GetNames<MapProviderEnum>().ToList()),
                new PropertySettingDefinition(MapStyleUrl, map, "Map style URL", "MapLibre style JSON URL.", SettingTypeEnum.String, maxLength: 500),
                new PropertySettingDefinition(MapDarkStyleUrl, map, "Dark map style URL", "MapLibre style JSON URL for dark mode.", SettingTypeEnum.String, maxLength: 500),
                new PropertySettingDefinition(MapPublicKey, map, "Public tile key", "Client-visible key restricted at the provider by referrer and app id (never logged).", SettingTypeEnum.String, maxLength: 200),
                new PropertySettingDefinition(MapAttribution, map, "Map attribution", "Text or Markdown link shown on the map.", SettingTypeEnum.String, maxLength: 500),
                new PropertySettingDefinition(MapTileProxy, map, "Tile proxy", "Route style and tile requests through GET /api/v1/directory/tiles/{*path}, adding the proxy key server-side.", SettingTypeEnum.Boolean, "false"),
                new PropertySettingDefinition(MapProxyKey, map, "Tile proxy key", "Secret key the tile proxy adds (stored encrypted).", SettingTypeEnum.Secret),
                new PropertySettingDefinition(MapProxyKeyParameter, map, "Tile proxy key parameter", "Query parameter that carries the proxy key. Default key.", SettingTypeEnum.String, "key", maxLength: 40),
                new PropertySettingDefinition(MapTileBaseUrl, map, "Tile origin", "The only origin the tile proxy fetches; empty uses the origin of the style URL.", SettingTypeEnum.String, maxLength: 500),
                new PropertySettingDefinition(MapDefaultCenterLat, map, "Default center latitude", "-90 to 90. Default 0.", SettingTypeEnum.Decimal, "0", maxLength: 40),
                new PropertySettingDefinition(MapDefaultCenterLng, map, "Default center longitude", "-180 to 180. Default 0.", SettingTypeEnum.Decimal, "0", maxLength: 40),
                new PropertySettingDefinition(MapDefaultZoom, map, "Default zoom", "0 to 22. Default 2.", SettingTypeEnum.Integer, "2", null, 0, 22),
                new PropertySettingDefinition(MapMaxZoom, map, "Maximum zoom", "0 to 22. Default 18.", SettingTypeEnum.Integer, "18", null, 0, 22),
                new PropertySettingDefinition(MapMaxFeatures, map, "Map features per query", "50 to 1000. Default 500.", SettingTypeEnum.Integer, "500", null, 50, 1000)
            };
        }

        #endregion
    }
}
