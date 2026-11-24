namespace TrellisKit.Infra.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Amazon.CDK.Assertions;
    using Xunit;

    /// <summary>
    /// Website, media, and redirect distribution assertions.
    /// </summary>
    public class CdnTests
    {
        private static readonly string _CachingDisabledPolicyId = "4135ea2d-6df8-44a3-9df3-4b5a84be39ad";
        private static readonly string _AllViewerExceptHostHeaderPolicyId = "b689b0a8-53d0-40ab-baf2-68738e2966ac";

        /// <summary>
        /// /api/* goes to the API origin uncached with all methods and every viewer header except Host.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void ApiBehaviorForwardsEverythingUncached(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            JsonElement config = DistributionConfig(env, env.Stacks.Web);

            JsonElement api = config.GetProperty("CacheBehaviors").EnumerateArray()
                .Single(b => b.GetProperty("PathPattern").GetString() == "/api/*");
            Assert.Equal(_CachingDisabledPolicyId, api.GetProperty("CachePolicyId").GetString());
            Assert.Equal(_AllViewerExceptHostHeaderPolicyId, api.GetProperty("OriginRequestPolicyId").GetString());
            List<string> methods = api.GetProperty("AllowedMethods").EnumerateArray().Select(m => m.GetString() ?? string.Empty).ToList();
            Assert.Contains("POST", methods);
            Assert.Contains("DELETE", methods);

            string originId = api.GetProperty("TargetOriginId").GetString() ?? string.Empty;
            JsonElement origin = config.GetProperty("Origins").EnumerateArray().Single(o => o.GetProperty("Id").GetString() == originId);
            Assert.Equal(env.Stacks.Config.Domains.ApiHost, origin.GetProperty("DomainName").GetString());
            Assert.Equal("https-only", origin.GetProperty("CustomOriginConfig").GetProperty("OriginProtocolPolicy").GetString());
        }

        /// <summary>
        /// The website serves both hosts, routes SPA paths through the viewer-request function, sends security
        /// headers, and does not use distribution-wide error responses (which would rewrite API errors).
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void WebsiteHasSpaRoutingAndSecurityHeaders(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            JsonElement config = DistributionConfig(env, env.Stacks.Web);
            List<string> aliases = config.GetProperty("Aliases").EnumerateArray().Select(a => a.GetString() ?? string.Empty).ToList();
            Assert.Contains(env.Stacks.Config.Domains.PrimaryDomain, aliases);
            Assert.Contains(env.Stacks.Config.Domains.WwwHost, aliases);
            Assert.False(config.TryGetProperty("CustomErrorResponses", out JsonElement _));
            Assert.Equal("TLSv1.2_2021", config.GetProperty("ViewerCertificate").GetProperty("MinimumProtocolVersion").GetString());

            JsonElement association = config.GetProperty("DefaultCacheBehavior").GetProperty("FunctionAssociations")[0];
            Assert.Equal("viewer-request", association.GetProperty("EventType").GetString());

            JsonElement function = env.Resources(env.Stacks.Web, "AWS::CloudFront::Function")
                .Single(f => (f.GetProperty("Properties").GetProperty("FunctionCode").GetString() ?? string.Empty).Contains("SPA_SHELL"));
            string code = function.GetProperty("Properties").GetProperty("FunctionCode").GetString() ?? string.Empty;
            Assert.Contains("SPA_SHELL = '/index.html'", code);
            Assert.Contains("WWW_HOST = '" + env.Stacks.Config.Domains.WwwHost + "'", code);

            env.Template(env.Stacks.Web).HasResourceProperties("AWS::CloudFront::ResponseHeadersPolicy", new Dictionary<string, object>
            {
                ["ResponseHeadersPolicyConfig"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["SecurityHeadersConfig"] = Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["StrictTransportSecurity"] = Match.ObjectLike(new Dictionary<string, object> { ["IncludeSubdomains"] = true }),
                        ["FrameOptions"] = Match.ObjectLike(new Dictionary<string, object> { ["FrameOption"] = "DENY" }),
                        ["ContentTypeOptions"] = Match.AnyValue()
                    })
                })
            });
        }

        /// <summary>
        /// The vanity domain answers with a 301 to the website, preserving path and query.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev-redirect")]
        [InlineData("prod-redirect")]
        public void RedirectFunctionIssuesPermanentRedirect(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            JsonElement function = env.Resources(env.Stacks.Redirect, "AWS::CloudFront::Function").Single();
            JsonElement properties = function.GetProperty("Properties");
            Assert.Equal("cloudfront-js-2.0", properties.GetProperty("FunctionConfig").GetProperty("Runtime").GetString());
            string code = properties.GetProperty("FunctionCode").GetString() ?? string.Empty;
            Assert.Contains("statusCode: 301", code);
            Assert.Contains("TARGET = '" + env.Stacks.Config.Domains.RedirectTargetUrl + "'", code);
            Assert.Contains("TARGET + request.uri + query", code);

            JsonElement config = DistributionConfig(env, env.Stacks.Redirect);
            List<string> aliases = config.GetProperty("Aliases").EnumerateArray().Select(a => a.GetString() ?? string.Empty).ToList();
            string vanity = env.Stacks.Config.Domains.RedirectDomains.Single();
            Assert.EndsWith(Environments.VanityDomain, vanity);
            Assert.Contains(vanity, aliases);
            Assert.Contains(env.Stacks.Config.Domains.RedirectWwwHost(vanity), aliases);
            env.Template(env.Stacks.Redirect).ResourceCountIs("AWS::Route53::RecordSet", 4);
        }

        /// <summary>
        /// Media requires signed cookies except under the public path, and MediaConvert events reach the webhook.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void MediaRequiresSignedCookiesAndForwardsJobEvents(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            JsonElement config = DistributionConfig(env, env.Stacks.Media);
            Assert.True(config.GetProperty("DefaultCacheBehavior").TryGetProperty("TrustedKeyGroups", out JsonElement _));
            JsonElement publicBehavior = config.GetProperty("CacheBehaviors").EnumerateArray()
                .Single(b => b.GetProperty("PathPattern").GetString() == env.Stacks.Config.Media.PublicPathPattern);
            Assert.False(publicBehavior.TryGetProperty("TrustedKeyGroups", out JsonElement _));
            List<JsonElement> behaviors = config.GetProperty("CacheBehaviors").EnumerateArray().ToList();
            List<string> patterns = behaviors.Select(b => b.GetProperty("PathPattern").GetString() ?? string.Empty).ToList();
            Assert.True(patterns.IndexOf(env.Stacks.Config.Media.PublicVideoPathPattern) < patterns.IndexOf(env.Stacks.Config.Media.PublicPathPattern));
            Assert.True(behaviors.Single(b => b.GetProperty("PathPattern").GetString() == env.Stacks.Config.Media.VideoPathPattern).TryGetProperty("TrustedKeyGroups", out JsonElement _));
            Assert.False(behaviors.Single(b => b.GetProperty("PathPattern").GetString() == env.Stacks.Config.Media.PublicVideoPathPattern).TryGetProperty("TrustedKeyGroups", out JsonElement _));
            Assert.Equal(3, config.GetProperty("Origins").GetArrayLength());

            Template template = env.Template(env.Stacks.Media);
            template.ResourceCountIs("AWS::CloudFront::KeyGroup", 1);
            template.HasResourceProperties("AWS::Events::Rule", new Dictionary<string, object>
            {
                ["EventPattern"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["source"] = new string[] { "aws.mediaconvert" },
                    ["detail-type"] = new string[] { "MediaConvert Job State Change" }
                })
            });
            template.HasResourceProperties("AWS::SNS::Subscription", new Dictionary<string, object>
            {
                ["Protocol"] = "https",
                ["Endpoint"] = env.Stacks.Config.Domains.ApiUrl + "/api/v1/webhooks/mediaconvert"
            });
            template.HasResourceProperties("AWS::IAM::Role", new Dictionary<string, object>
            {
                ["AssumeRolePolicyDocument"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["Statement"] = Match.ArrayWith(new object[]
                    {
                        Match.ObjectLike(new Dictionary<string, object>
                        {
                            ["Principal"] = new Dictionary<string, object> { ["Service"] = "mediaconvert.amazonaws.com" }
                        })
                    })
                })
            });
        }

        private static JsonElement DistributionConfig(SynthesizedEnvironment env, Amazon.CDK.Stack? stack)
        {
            JsonElement distribution = env.Resources(stack, "AWS::CloudFront::Distribution").Single();
            return distribution.GetProperty("Properties").GetProperty("DistributionConfig");
        }
    }
}
