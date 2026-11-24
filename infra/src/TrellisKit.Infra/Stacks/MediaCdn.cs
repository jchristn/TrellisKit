namespace TrellisKit.Infra.Stacks
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Amazon.CDK;
    using Amazon.CDK.AWS.CertificateManager;
    using Amazon.CDK.AWS.CloudFront;
    using Amazon.CDK.AWS.CloudFront.Origins;
    using Amazon.CDK.AWS.Route53;
    using Amazon.CDK.AWS.Route53.Targets;
    using Amazon.CDK.AWS.S3;
    using TrellisKit.Infra.Config;

    /// <summary>
    /// Builds one property's media host distribution: images bucket by default and transcoded bucket under the video
    /// path (/&lt;propertyId&gt;/video/...), both requiring signed cookies except under the public paths
    /// (/&lt;propertyId&gt;/public/...); the backend scopes each grant's cookie policy to /&lt;propertyId&gt;/*; and the live updates bucket under the
    /// updates path without cookies, with hashed assets cached immutably and everything else (manifests) never cached.
    /// Used by <see cref="MediaStack"/> (per-property topology) and <see cref="PropertyMediaStack"/> (shared topology).
    /// </summary>
    public static class MediaCdn
    {
        /// <summary>
        /// Create the distribution and its alias records directly in a stack (construct ids are stable across topologies).
        /// </summary>
        /// <param name="scope">Stack that owns the resources.</param>
        /// <param name="config">Configuration of the property.</param>
        /// <param name="dns">The property's hosted zones.</param>
        /// <param name="certificates">The property's CloudFront certificates (us-east-1).</param>
        /// <param name="storage">Buckets.</param>
        /// <param name="keyGroup">Key group that validates signed cookies.</param>
        /// <returns>The distribution.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public static Distribution Create(
            Stack scope,
            EnvironmentConfig config,
            DnsStack dns,
            EdgeCertificateStack certificates,
            StorageStack storage,
            IKeyGroup keyGroup)
        {
            ArgumentNullException.ThrowIfNull(scope);
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(dns);
            ArgumentNullException.ThrowIfNull(certificates);
            ArgumentNullException.ThrowIfNull(storage);
            ArgumentNullException.ThrowIfNull(keyGroup);

            IBucket images = Bucket.FromBucketAttributes(scope, "MediaImagesBucketRef", new BucketAttributes
            {
                BucketName = storage.MediaImagesBucket.BucketName,
                BucketRegionalDomainName = storage.MediaImagesBucket.BucketRegionalDomainName
            });
            IBucket transcoded = Bucket.FromBucketAttributes(scope, "MediaTranscodedBucketRef", new BucketAttributes
            {
                BucketName = storage.MediaTranscodedBucket.BucketName,
                BucketRegionalDomainName = storage.MediaTranscodedBucket.BucketRegionalDomainName
            });
            IBucket updates = Bucket.FromBucketAttributes(scope, "UpdatesBucketRef", new BucketAttributes
            {
                BucketName = storage.UpdatesBucket.BucketName,
                BucketRegionalDomainName = storage.UpdatesBucket.BucketRegionalDomainName
            });
            S3OriginAccessControl accessControl = new S3OriginAccessControl(scope, "MediaOriginAccessControl", new S3OriginAccessControlProps
            {
                OriginAccessControlName = config.ResourceName("media-oac"),
                Description = "Media distribution access to the images, transcoded, and updates buckets"
            });
            IOrigin origin = S3BucketOrigin.WithOriginAccessControl(images, new S3BucketOriginWithOACProps { OriginAccessControl = accessControl });
            IOrigin videoOrigin = S3BucketOrigin.WithOriginAccessControl(transcoded, new S3BucketOriginWithOACProps { OriginAccessControl = accessControl });
            IOrigin updatesOrigin = S3BucketOrigin.WithOriginAccessControl(updates, new S3BucketOriginWithOACProps { OriginAccessControl = accessControl });

            ResponseHeadersStrictTransportSecurity hsts = new ResponseHeadersStrictTransportSecurity
            {
                AccessControlMaxAge = Duration.Seconds(config.Web.HstsMaxAgeSeconds),
                IncludeSubdomains = true,
                Preload = config.Web.HstsPreload,
                Override = true
            };

            ResponseHeadersCorsBehavior cors = new ResponseHeadersCorsBehavior
            {
                AccessControlAllowCredentials = true,
                AccessControlAllowHeaders = new string[] { "Range", "Origin", "Accept" },
                AccessControlAllowMethods = new string[] { "GET", "HEAD", "OPTIONS" },
                AccessControlAllowOrigins = config.WebOrigins.ToArray(),
                AccessControlExposeHeaders = new string[] { "Content-Length", "Content-Range", "ETag" },
                AccessControlMaxAge = Duration.Seconds(config.Media.CorsMaxAgeSeconds),
                OriginOverride = true
            };
            ResponseHeadersPolicy corsPolicy = new ResponseHeadersPolicy(scope, "MediaCorsPolicy", new ResponseHeadersPolicyProps
            {
                ResponseHeadersPolicyName = config.ResourceName("media-cors"),
                CorsBehavior = cors,
                SecurityHeadersBehavior = new ResponseSecurityHeadersBehavior
                {
                    ContentTypeOptions = new ResponseHeadersContentTypeOptions { Override = true },
                    StrictTransportSecurity = hsts
                }
            });

            // Document media (PDF) gets the headers the backend sends when it serves documents itself: a PDF downloads
            // instead of rendering in the media origin and can never run script there.
            ResponseHeadersPolicy documentPolicy = new ResponseHeadersPolicy(scope, "MediaDocumentPolicy", new ResponseHeadersPolicyProps
            {
                ResponseHeadersPolicyName = config.ResourceName("media-documents"),
                Comment = "Document media: download as an attachment, sandboxed, never sniffed",
                CorsBehavior = cors,
                CustomHeadersBehavior = new ResponseCustomHeadersBehavior
                {
                    CustomHeaders = new IResponseCustomHeader[]
                    {
                        new ResponseCustomHeader { Header = "Content-Disposition", Value = "attachment", Override = true }
                    }
                },
                SecurityHeadersBehavior = new ResponseSecurityHeadersBehavior
                {
                    ContentSecurityPolicy = new ResponseHeadersContentSecurityPolicy { ContentSecurityPolicy = "sandbox", Override = true },
                    ContentTypeOptions = new ResponseHeadersContentTypeOptions { Override = true },
                    StrictTransportSecurity = hsts
                }
            });

            UpdatesSettings settings = config.Updates;
            Duration immutable = Duration.Seconds(settings.ImmutableMaxAgeSeconds);
            CachePolicy updatesAssetsCache = new CachePolicy(scope, "UpdatesAssetsCachePolicy", new CachePolicyProps
            {
                CachePolicyName = config.ResourceName("updates-immutable"),
                Comment = "Content-addressed live update assets: cache for the maximum time, key on the path only",
                DefaultTtl = immutable,
                MinTtl = immutable,
                MaxTtl = immutable,
                CookieBehavior = CacheCookieBehavior.None(),
                HeaderBehavior = CacheHeaderBehavior.None(),
                QueryStringBehavior = CacheQueryStringBehavior.None(),
                EnableAcceptEncodingGzip = true,
                EnableAcceptEncodingBrotli = true
            });
            ResponseHeadersPolicy updatesAssetsHeaders = new ResponseHeadersPolicy(scope, "UpdatesAssetsHeaders", new ResponseHeadersPolicyProps
            {
                ResponseHeadersPolicyName = config.ResourceName("updates-immutable"),
                CustomHeadersBehavior = new ResponseCustomHeadersBehavior
                {
                    CustomHeaders = new IResponseCustomHeader[]
                    {
                        new ResponseCustomHeader
                        {
                            Header = "Cache-Control",
                            Value = "public, max-age=" + settings.ImmutableMaxAgeSeconds.ToString(CultureInfo.InvariantCulture) + ", immutable",
                            Override = true
                        }
                    }
                },
                SecurityHeadersBehavior = new ResponseSecurityHeadersBehavior
                {
                    ContentTypeOptions = new ResponseHeadersContentTypeOptions { Override = true },
                    StrictTransportSecurity = hsts
                }
            });
            ResponseHeadersPolicy updatesNoCacheHeaders = new ResponseHeadersPolicy(scope, "UpdatesNoCacheHeaders", new ResponseHeadersPolicyProps
            {
                ResponseHeadersPolicyName = config.ResourceName("updates-no-cache"),
                CustomHeadersBehavior = new ResponseCustomHeadersBehavior
                {
                    CustomHeaders = new IResponseCustomHeader[]
                    {
                        new ResponseCustomHeader { Header = "Cache-Control", Value = "no-cache, no-store, must-revalidate", Override = true }
                    }
                },
                SecurityHeadersBehavior = new ResponseSecurityHeadersBehavior
                {
                    ContentTypeOptions = new ResponseHeadersContentTypeOptions { Override = true },
                    StrictTransportSecurity = hsts
                }
            });

            // Order matters: CloudFront evaluates behaviors in order. Media keys start with the property id, so the
            // media patterns begin with a wildcard segment (/*/video/*) and would also match an updates path that
            // happens to contain /video/ or /public/; the updates behaviors therefore come first, the hashed updates
            // assets before the rest of the updates path, the public document path before the document and public paths
            // (/*/media/*.pdf also matches /<id>/public/media/...), and the public video path before the video and
            // public paths.
            Dictionary<string, IBehaviorOptions> behaviors = new Dictionary<string, IBehaviorOptions>
            {
                // Live updates are anonymous (the app checks before sign-in) and integrity-checked by hash and the
                // manifest signature, so no signed cookies.
                [settings.AssetsPathPattern] = new BehaviorOptions
                {
                    Origin = updatesOrigin,
                    ViewerProtocolPolicy = ViewerProtocolPolicy.HTTPS_ONLY,
                    AllowedMethods = AllowedMethods.ALLOW_GET_HEAD,
                    CachedMethods = CachedMethods.CACHE_GET_HEAD,
                    CachePolicy = updatesAssetsCache,
                    ResponseHeadersPolicy = updatesAssetsHeaders,
                    Compress = true
                },
                [settings.PathPattern] = new BehaviorOptions
                {
                    Origin = updatesOrigin,
                    ViewerProtocolPolicy = ViewerProtocolPolicy.HTTPS_ONLY,
                    AllowedMethods = AllowedMethods.ALLOW_GET_HEAD,
                    CachedMethods = CachedMethods.CACHE_GET_HEAD,
                    CachePolicy = CachePolicy.CACHING_DISABLED,
                    ResponseHeadersPolicy = updatesNoCacheHeaders,
                    Compress = true
                },
                [config.Media.PublicDocumentPathPattern] = MediaBehavior(origin, documentPolicy, null),
                [config.Media.DocumentPathPattern] = MediaBehavior(origin, documentPolicy, keyGroup),
                [config.Media.PublicVideoPathPattern] = MediaBehavior(videoOrigin, corsPolicy, null),
                [config.Media.VideoPathPattern] = MediaBehavior(videoOrigin, corsPolicy, keyGroup),
                [config.Media.PublicPathPattern] = MediaBehavior(origin, corsPolicy, null)
            };

            Distribution distribution = new Distribution(scope, "MediaDistribution", new DistributionProps
            {
                Comment = config.Domains.MediaHost,
                DomainNames = new string[] { config.Domains.MediaHost },
                Certificate = certificates.MediaCertificate,
                MinimumProtocolVersion = SecurityPolicyProtocol.TLS_V1_2_2021,
                HttpVersion = HttpVersion.HTTP2_AND_3,
                EnableIpv6 = true,
                PriceClass = config.Media.PriceClass,
                EnableLogging = true,
                LogBucket = storage.AccessLogBucket,
                LogFilePrefix = "cloudfront/media/",
                DefaultBehavior = MediaBehavior(origin, corsPolicy, keyGroup),
                AdditionalBehaviors = behaviors
            });
            CloudFrontNag.SuppressCommon(distribution, true);

            if (config.Dns.IsExternal)
            {
                DnsRecordOutputs.AddTarget(scope, "Media", config, config.Domains.MediaHost, distribution.DistributionDomainName,
                    "Media and live updates CDN (https://" + config.Domains.MediaHost + " and https://" + config.Domains.MediaHost + config.Updates.PathPrefix + ")");
                new CertificateIssuedCheck(scope, "CertificateCheck", config, "certificate-check-media",
                    new ICertificate[] { certificates.MediaCertificate }, certificates.PendingHint).Gate(scope);
                return distribution;
            }

            new ARecord(scope, "MediaAliasA", new ARecordProps
            {
                Zone = dns.PrimaryZone!,
                RecordName = config.Domains.MediaHost,
                Target = RecordTarget.FromAlias(new CloudFrontTarget(distribution))
            });
            new AaaaRecord(scope, "MediaAliasAaaa", new AaaaRecordProps
            {
                Zone = dns.PrimaryZone!,
                RecordName = config.Domains.MediaHost,
                Target = RecordTarget.FromAlias(new CloudFrontTarget(distribution))
            });

            return distribution;
        }

        private static BehaviorOptions MediaBehavior(IOrigin origin, IResponseHeadersPolicy corsPolicy, IKeyGroup? keyGroup)
        {
            BehaviorOptions behavior = new BehaviorOptions
            {
                Origin = origin,
                ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                AllowedMethods = AllowedMethods.ALLOW_GET_HEAD_OPTIONS,
                CachedMethods = CachedMethods.CACHE_GET_HEAD_OPTIONS,
                CachePolicy = CachePolicy.CACHING_OPTIMIZED,
                OriginRequestPolicy = OriginRequestPolicy.CORS_S3_ORIGIN,
                ResponseHeadersPolicy = corsPolicy,
                Compress = true
            };
            if (keyGroup != null) behavior.TrustedKeyGroups = new IKeyGroup[] { keyGroup };
            return behavior;
        }
    }
}
