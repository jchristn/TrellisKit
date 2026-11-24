namespace TrellisKit.Infra.Stacks
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Amazon.CDK;
    using Amazon.CDK.AWS.CertificateManager;
    using Amazon.CDK.AWS.CloudFront;
    using Amazon.CDK.AWS.CloudFront.Origins;
    using Amazon.CDK.AWS.Route53;
    using Amazon.CDK.AWS.Route53.Targets;
    using Amazon.CDK.AWS.S3;
    using TrellisKit.Infra.Config;
    using Constructs;
    using Function = Amazon.CDK.AWS.CloudFront.Function;

    /// <summary>
    /// Website distribution for the apex and www hosts. The default behavior serves the S3 build through Origin
    /// Access Control with a viewer-request function that redirects www to the apex and rewrites client-rendered
    /// routes (/app/*, /admin/*, and any other extensionless path) to the SPA shell. The API path patterns go to the
    /// ALB uncached (CachingDisabled, so no response is ever shared between viewers or properties) with every header
    /// (except Host, including X-TK-Property and X-TK-App), cookie, and query string, so the website calls the API
    /// same-origin. A viewer-request function on the API behaviors overwrites X-Forwarded-Host with the viewer's host,
    /// so the server resolves the property from the website domain the request was made to; CloudFront adds the
    /// viewer address to X-Forwarded-For and sends CloudFront-Viewer-Address. The live update and app manifest paths get
    /// their own uncached API behaviors ahead of the API patterns, and the assistant embed page (/embed/assistants/*)
    /// gets a headers policy with Content-Security-Policy frame-ancestors instead of X-Frame-Options DENY. The API origin is the load balancer's
    /// CloudFront origin listener (ApiSettings.CloudFrontOriginHttpsPort), which admits only CloudFront. Distribution-wide custom error responses are deliberately not used: they would also rewrite API
    /// 403 and 404 responses into the SPA shell. In the per-property topology the website bucket lives in the
    /// storage stack; in the shared topology each property's web stack owns its website bucket, readable only by its
    /// own distribution.
    /// </summary>
    public class WebStack : Stack
    {
        /// <summary>
        /// The website distribution.
        /// </summary>
        public Distribution Distribution { get; }

        /// <summary>
        /// Viewer-request function of the default behavior.
        /// </summary>
        public Function RoutingFunction { get; }

        /// <summary>
        /// Viewer-request function of the API behaviors: sets X-Forwarded-Host to the viewer's Host header.
        /// </summary>
        public Function ApiForwardingFunction { get; }

        /// <summary>
        /// The website bucket owned by this stack (shared topology), or null when it lives in the storage stack.
        /// </summary>
        public Bucket? OwnedWebsiteBucket { get; }

        /// <summary>
        /// The website bucket this distribution serves, owned here or in the storage stack.
        /// </summary>
        public IBucket WebsiteBucket { get; }

        /// <summary>
        /// Instantiate the stack.
        /// </summary>
        /// <param name="scope">Parent construct.</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Environment configuration.</param>
        /// <param name="dns">Hosted zones.</param>
        /// <param name="certificates">CloudFront certificates (us-east-1).</param>
        /// <param name="storage">Buckets.</param>
        /// <param name="props">Stack properties (cross-region references enabled).</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public WebStack(
            Construct scope,
            string id,
            EnvironmentConfig config,
            DnsStack dns,
            EdgeCertificateStack certificates,
            StorageStack storage,
            IStackProps props) : base(scope, id, props)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(dns);
            ArgumentNullException.ThrowIfNull(certificates);
            ArgumentNullException.ThrowIfNull(storage);

            WebSettings web = config.Web;

            if (storage.WebsiteBucket != null)
            {
                WebsiteBucket = Bucket.FromBucketAttributes(this, "WebsiteBucketRef", new BucketAttributes
                {
                    BucketName = storage.WebsiteBucket.BucketName,
                    BucketRegionalDomainName = storage.WebsiteBucket.BucketRegionalDomainName
                });
            }
            else
            {
                // Imported by name: adding the log delivery grant to the shared bucket from here would create a cycle
                // (the storage stack grants it for every website bucket of the account instead).
                IBucket logs = Bucket.FromBucketName(this, "AccessLogBucketRef", storage.AccessLogBucket.BucketName);
                OwnedWebsiteBucket = new Bucket(this, "WebsiteBucket", new BucketProps
                {
                    BucketName = config.BucketName("website"),
                    BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
                    Encryption = BucketEncryption.S3_MANAGED,
                    EnforceSSL = true,
                    ObjectOwnership = ObjectOwnership.BUCKET_OWNER_ENFORCED,
                    Versioned = true,
                    ServerAccessLogsBucket = logs,
                    ServerAccessLogsPrefix = StorageStack.SiteAccessLogPrefix + config.Property.Slug + "/",
                    RemovalPolicy = config.StatefulRemovalPolicy,
                    LifecycleRules = new ILifecycleRule[]
                    {
                        new LifecycleRule
                        {
                            Id = "expire-old-builds",
                            NoncurrentVersionExpiration = Duration.Days(config.Storage.NoncurrentVersionExpirationDays),
                            AbortIncompleteMultipartUploadAfter = Duration.Days(config.Storage.AbortIncompleteMultipartUploadDays)
                        }
                    }
                });
                Annotations.Of(OwnedWebsiteBucket).AcknowledgeWarning("@aws-cdk/aws-s3:accessLogsPolicyNotAdded",
                    "StorageStack grants log delivery for every website bucket of the account under the site access log prefix.");
                WebsiteBucket = OwnedWebsiteBucket;
                new CfnOutput(this, "WebsiteBucketName", new CfnOutputProps { Value = OwnedWebsiteBucket.BucketName });
            }

            // With an owned bucket CDK scopes the read grant to this distribution only.
            IOrigin siteOrigin = S3BucketOrigin.WithOriginAccessControl(WebsiteBucket);

            IOrigin apiOrigin = new HttpOrigin(config.Domains.ApiOriginHost, new HttpOriginProps
            {
                ProtocolPolicy = OriginProtocolPolicy.HTTPS_ONLY,
                // The load balancer's CloudFront origin listener, reachable only from CloudFront's origin-facing ranges.
                HttpsPort = config.Api.CloudFrontOriginHttpsPort,
                OriginSslProtocols = new OriginSslPolicy[] { OriginSslPolicy.TLS_V1_2 },
                ReadTimeout = Duration.Seconds(web.ApiOriginReadTimeoutSeconds)
            });

            RoutingFunction = new Function(this, "RoutingFunction", new FunctionProps
            {
                FunctionName = config.ResourceName("web-routing"),
                Comment = "www to apex redirect and SPA routing",
                Runtime = FunctionRuntime.JS_2_0,
                Code = FunctionCode.FromInline(BuildRoutingFunction(config))
            });

            ApiForwardingFunction = new Function(this, "ApiForwardingFunction", new FunctionProps
            {
                FunctionName = config.ResourceName("web-api-forwarding"),
                Comment = "X-Forwarded-Host from the viewer host for property resolution",
                Runtime = FunctionRuntime.JS_2_0,
                Code = FunctionCode.FromInline(BuildApiForwardingFunction())
            });

            ResponseHeadersPolicy securityHeaders = new ResponseHeadersPolicy(this, "SecurityHeaders", new ResponseHeadersPolicyProps
            {
                ResponseHeadersPolicyName = config.ResourceName("web-security-headers"),
                SecurityHeadersBehavior = BuildSecurityHeaders(web),
                CustomHeadersBehavior = new ResponseCustomHeadersBehavior
                {
                    CustomHeaders = new IResponseCustomHeader[]
                    {
                        new ResponseCustomHeader { Header = "Permissions-Policy", Value = web.PermissionsPolicy, Override = true }
                    }
                }
            });

            BehaviorOptions apiBehavior = new BehaviorOptions
            {
                Origin = apiOrigin,
                ViewerProtocolPolicy = ViewerProtocolPolicy.HTTPS_ONLY,
                AllowedMethods = AllowedMethods.ALLOW_ALL,
                CachePolicy = CachePolicy.CACHING_DISABLED,
                // All viewer headers, cookies, and query strings. Host is excluded because CloudFront validates
                // the origin certificate (api host) against the forwarded Host header. The managed policy also adds
                // CloudFront's viewer location headers, among them CloudFront-Viewer-Address (viewer ip:port), which
                // the server reads as the client address (rest.viewerAddressHeader). A custom policy cannot express
                // "every viewer header except Host plus CloudFront headers" (allExcept takes no CloudFront headers,
                // and allViewerAndWhitelistCloudFront forwards Host).
                OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
                ResponseHeadersPolicy = securityHeaders,
                Compress = false,
                FunctionAssociations = new IFunctionAssociation[]
                {
                    new FunctionAssociation { Function = ApiForwardingFunction, EventType = FunctionEventType.VIEWER_REQUEST }
                }
            };

            // The assistant embed page may be framed by the configured origins: frame-ancestors replaces
            // X-Frame-Options DENY on this path only.
            ResponseHeadersPolicy embedHeaders = new ResponseHeadersPolicy(this, "EmbedHeaders", new ResponseHeadersPolicyProps
            {
                ResponseHeadersPolicyName = config.ResourceName("web-embed-headers"),
                Comment = "Assistant embed page: frame-ancestors instead of X-Frame-Options",
                SecurityHeadersBehavior = BuildEmbedSecurityHeaders(web, config.Ai),
                CustomHeadersBehavior = new ResponseCustomHeadersBehavior
                {
                    CustomHeaders = new IResponseCustomHeader[]
                    {
                        new ResponseCustomHeader { Header = "Permissions-Policy", Value = web.PermissionsPolicy, Override = true }
                    }
                }
            });

            // Order matters: the manifest paths come before the API patterns they also match (/api/*).
            Dictionary<string, IBehaviorOptions> additional = new Dictionary<string, IBehaviorOptions>();
            foreach (string pattern in web.ManifestPathPatterns) additional[pattern] = ManifestBehavior(apiOrigin, securityHeaders);
            foreach (string pattern in web.ApiPathPatterns) additional[pattern] = apiBehavior;
            additional[web.EmbedPathPattern] = new BehaviorOptions
            {
                Origin = siteOrigin,
                ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                AllowedMethods = AllowedMethods.ALLOW_GET_HEAD_OPTIONS,
                CachePolicy = CachePolicy.CACHING_OPTIMIZED,
                ResponseHeadersPolicy = embedHeaders,
                Compress = true,
                FunctionAssociations = new IFunctionAssociation[]
                {
                    new FunctionAssociation { Function = RoutingFunction, EventType = FunctionEventType.VIEWER_REQUEST }
                }
            };

            Distribution = new Distribution(this, "WebDistribution", new DistributionProps
            {
                Comment = config.Domains.PrimaryDomain,
                DomainNames = new string[] { config.Domains.PrimaryDomain, config.Domains.WwwHost },
                Certificate = certificates.WebCertificate,
                MinimumProtocolVersion = SecurityPolicyProtocol.TLS_V1_2_2021,
                HttpVersion = HttpVersion.HTTP2_AND_3,
                EnableIpv6 = true,
                PriceClass = web.PriceClass,
                DefaultRootObject = "index.html",
                EnableLogging = true,
                LogBucket = storage.AccessLogBucket,
                LogFilePrefix = "cloudfront/web/",
                DefaultBehavior = new BehaviorOptions
                {
                    Origin = siteOrigin,
                    ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                    AllowedMethods = AllowedMethods.ALLOW_GET_HEAD_OPTIONS,
                    CachePolicy = CachePolicy.CACHING_OPTIMIZED,
                    ResponseHeadersPolicy = securityHeaders,
                    Compress = true,
                    FunctionAssociations = new IFunctionAssociation[]
                    {
                        new FunctionAssociation { Function = RoutingFunction, EventType = FunctionEventType.VIEWER_REQUEST }
                    }
                },
                AdditionalBehaviors = additional
            });
            CloudFrontNag.SuppressCommon(Distribution, OwnedWebsiteBucket == null);

            if (config.Dns.IsExternal)
            {
                DnsRecordOutputs.AddTarget(this, "WebApex", config, config.Domains.PrimaryDomain, Distribution.DistributionDomainName, "Website");
                DnsRecordOutputs.AddTarget(this, "WebWww", config, config.Domains.WwwHost, Distribution.DistributionDomainName, "Website www host (redirects to the apex)");
            }
            else
            {
                foreach (string host in new string[] { config.Domains.PrimaryDomain, config.Domains.WwwHost })
                {
                    string suffix = host == config.Domains.PrimaryDomain ? "Apex" : "Www";
                    new ARecord(this, "WebAliasA" + suffix, new ARecordProps
                    {
                        Zone = dns.PrimaryZone!,
                        RecordName = host,
                        Target = RecordTarget.FromAlias(new CloudFrontTarget(Distribution))
                    });
                    new AaaaRecord(this, "WebAliasAaaa" + suffix, new AaaaRecordProps
                    {
                        Zone = dns.PrimaryZone!,
                        RecordName = host,
                        Target = RecordTarget.FromAlias(new CloudFrontTarget(Distribution))
                    });
                }
            }

            new CfnOutput(this, "WebDistributionId", new CfnOutputProps { Value = Distribution.DistributionId });
            new CfnOutput(this, "WebDistributionDomain", new CfnOutputProps { Value = Distribution.DistributionDomainName });

            if (config.Dns.IsExternal)
            {
                new CertificateIssuedCheck(this, "CertificateCheck", config, "certificate-check-web",
                    new ICertificate[] { certificates.WebCertificate }, certificates.PendingHint).Gate(this);
            }
        }

        private BehaviorOptions ManifestBehavior(IOrigin apiOrigin, IResponseHeadersPolicy securityHeaders)
        {
            // Never cached, and every viewer header except Host plus the CloudFront viewer headers reach the origin.
            // A custom origin request policy cannot express the expo-*, X-TK-*, and X-Client* families (allow lists
            // take exact names, ten by default), so the managed AllViewerExceptHostHeader policy is used: it is a
            // superset of the contract's list and includes CloudFront-Viewer-Country and CloudFront-Viewer-Address.
            return new BehaviorOptions
            {
                Origin = apiOrigin,
                ViewerProtocolPolicy = ViewerProtocolPolicy.HTTPS_ONLY,
                AllowedMethods = AllowedMethods.ALLOW_GET_HEAD_OPTIONS,
                CachePolicy = CachePolicy.CACHING_DISABLED,
                OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
                ResponseHeadersPolicy = securityHeaders,
                Compress = false,
                FunctionAssociations = new IFunctionAssociation[]
                {
                    new FunctionAssociation { Function = ApiForwardingFunction, EventType = FunctionEventType.VIEWER_REQUEST }
                }
            };
        }

        private static IResponseSecurityHeadersBehavior BuildEmbedSecurityHeaders(WebSettings web, AiSettings ai)
        {
            string frameAncestors = "frame-ancestors " + ai.FrameAncestorsSources;
            string policy = String.IsNullOrWhiteSpace(web.ContentSecurityPolicy)
                ? frameAncestors
                : web.ContentSecurityPolicy.Trim().TrimEnd(';') + "; " + frameAncestors;

            return new ResponseSecurityHeadersBehavior
            {
                ContentTypeOptions = new ResponseHeadersContentTypeOptions { Override = true },
                ContentSecurityPolicy = new ResponseHeadersContentSecurityPolicy { ContentSecurityPolicy = policy, Override = true },
                ReferrerPolicy = new ResponseHeadersReferrerPolicy { ReferrerPolicy = HeadersReferrerPolicy.STRICT_ORIGIN_WHEN_CROSS_ORIGIN, Override = true },
                StrictTransportSecurity = new ResponseHeadersStrictTransportSecurity
                {
                    AccessControlMaxAge = Duration.Seconds(web.HstsMaxAgeSeconds),
                    IncludeSubdomains = true,
                    Preload = web.HstsPreload,
                    Override = true
                },
                XssProtection = new ResponseHeadersXSSProtection { Protection = true, ModeBlock = true, Override = true }
            };
        }

        private static IResponseSecurityHeadersBehavior BuildSecurityHeaders(WebSettings web)
        {
            ResponseSecurityHeadersBehavior headers = new ResponseSecurityHeadersBehavior
            {
                ContentTypeOptions = new ResponseHeadersContentTypeOptions { Override = true },
                FrameOptions = new ResponseHeadersFrameOptions { FrameOption = HeadersFrameOption.DENY, Override = true },
                ReferrerPolicy = new ResponseHeadersReferrerPolicy { ReferrerPolicy = HeadersReferrerPolicy.STRICT_ORIGIN_WHEN_CROSS_ORIGIN, Override = true },
                StrictTransportSecurity = new ResponseHeadersStrictTransportSecurity
                {
                    AccessControlMaxAge = Duration.Seconds(web.HstsMaxAgeSeconds),
                    IncludeSubdomains = true,
                    Preload = web.HstsPreload,
                    Override = true
                },
                XssProtection = new ResponseHeadersXSSProtection { Protection = true, ModeBlock = true, Override = true }
            };

            if (!String.IsNullOrWhiteSpace(web.ContentSecurityPolicy))
            {
                headers.ContentSecurityPolicy = new ResponseHeadersContentSecurityPolicy
                {
                    ContentSecurityPolicy = web.ContentSecurityPolicy,
                    Override = true
                };
            }

            return headers;
        }

        private static string BuildApiForwardingFunction()
        {
            // A client-supplied X-Forwarded-Host is always replaced (or removed), never passed through.
            StringBuilder js = new StringBuilder();
            js.Append("function handler(event) {\n");
            js.Append("  var request = event.request;\n");
            js.Append("  if (request.headers.host) { request.headers['x-forwarded-host'] = { value: request.headers.host.value }; }\n");
            js.Append("  else { delete request.headers['x-forwarded-host']; }\n");
            js.Append("  return request;\n");
            js.Append("}\n");
            return js.ToString();
        }

        private static string BuildRoutingFunction(EnvironmentConfig config)
        {
            StringBuilder routes = new StringBuilder();
            foreach (string route in config.Web.PrerenderedRoutes)
            {
                if (routes.Length > 0) routes.Append(", ");
                routes.Append("'" + route.TrimEnd('/') + "': true");
            }

            StringBuilder js = new StringBuilder();
            js.Append("var WWW_HOST = '" + config.Domains.WwwHost + "';\n");
            js.Append("var APEX_URL = '" + config.Domains.WebsiteUrl + "';\n");
            js.Append("var SPA_SHELL = '" + config.Web.SpaShellPath + "';\n");
            js.Append("var PRERENDERED = { " + routes.ToString() + " };\n");
            js.Append("function serializeQuery(qs) {\n");
            js.Append("  var parts = [];\n");
            js.Append("  for (var key in qs) {\n");
            js.Append("    var item = qs[key];\n");
            js.Append("    if (item.multiValue) { for (var i = 0; i < item.multiValue.length; i++) { parts.push(key + '=' + item.multiValue[i].value); } }\n");
            js.Append("    else if (item.value === '') { parts.push(key); }\n");
            js.Append("    else { parts.push(key + '=' + item.value); }\n");
            js.Append("  }\n");
            js.Append("  return parts.length > 0 ? '?' + parts.join('&') : '';\n");
            js.Append("}\n");
            js.Append("function handler(event) {\n");
            js.Append("  var request = event.request;\n");
            js.Append("  var host = request.headers.host ? request.headers.host.value : '';\n");
            js.Append("  if (host === WWW_HOST) {\n");
            js.Append("    return { statusCode: 301, statusDescription: 'Moved Permanently', headers: { location: { value: APEX_URL + request.uri + serializeQuery(request.querystring) } } };\n");
            js.Append("  }\n");
            js.Append("  var uri = request.uri;\n");
            js.Append("  var last = uri.substring(uri.lastIndexOf('/') + 1);\n");
            js.Append("  if (last.indexOf('.') !== -1) { return request; }\n");
            js.Append("  var trimmed = uri.length > 1 && uri.charAt(uri.length - 1) === '/' ? uri.substring(0, uri.length - 1) : uri;\n");
            js.Append("  if (trimmed === '/' || trimmed === '') { return request; }\n");
            js.Append("  if (PRERENDERED[trimmed]) { request.uri = trimmed + '/index.html'; return request; }\n");
            js.Append("  request.uri = SPA_SHELL;\n");
            js.Append("  return request;\n");
            js.Append("}\n");
            return js.ToString();
        }
    }
}
