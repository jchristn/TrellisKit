namespace TrellisKit.Infra.Config
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Text.RegularExpressions;

    /// <summary>
    /// VPC layout. Availability zones are listed explicitly so synthesis never looks up live AWS state.
    /// </summary>
    public class NetworkSettings
    {
        private static readonly Regex _PrefixListPattern = new Regex("^pl-[0-9a-f]{8,17}$");

        private string _CidrBlock = "10.40.0.0/16";
        private List<string> _AvailabilityZones = new List<string> { "us-west-2a", "us-west-2b" };
        private int _NatGateways = 1;
        private int _PublicSubnetMask = 24;
        private int _PrivateSubnetMask = 22;
        private int _IsolatedSubnetMask = 24;
        private string _CloudFrontOriginFacingPrefixListId = "pl-82a045eb";
        private List<string> _AdditionalTrustedProxies = new List<string>();

        /// <summary>
        /// IPv4 CIDR of the VPC. Default 10.40.0.0/16.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string CidrBlock
        {
            get { return _CidrBlock; }
            set { _CidrBlock = Guard.NotEmpty(value, nameof(CidrBlock)); }
        }

        /// <summary>
        /// Availability zones used by every subnet tier. Default us-west-2a and us-west-2b (two AZs).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public List<string> AvailabilityZones
        {
            get { return _AvailabilityZones; }
            set { _AvailabilityZones = Guard.NotNull(value, nameof(AvailabilityZones)); }
        }

        /// <summary>
        /// NAT gateways for private subnet egress. Default 1. Minimum 1, maximum 2.
        /// One gateway saves about 33 USD a month; two keep egress available if an AZ fails.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int NatGateways
        {
            get { return _NatGateways; }
            set { _NatGateways = Guard.Range(value, 1, 2, nameof(NatGateways)); }
        }

        /// <summary>
        /// Prefix length of each public subnet (ALB, NAT). Default 24. Minimum 16, maximum 28.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int PublicSubnetMask
        {
            get { return _PublicSubnetMask; }
            set { _PublicSubnetMask = Guard.Range(value, 16, 28, nameof(PublicSubnetMask)); }
        }

        /// <summary>
        /// Prefix length of each private subnet with NAT egress (Fargate tasks). Default 22. Minimum 16, maximum 28.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int PrivateSubnetMask
        {
            get { return _PrivateSubnetMask; }
            set { _PrivateSubnetMask = Guard.Range(value, 16, 28, nameof(PrivateSubnetMask)); }
        }

        /// <summary>
        /// Prefix length of each isolated subnet without internet egress (RDS). Default 24. Minimum 16, maximum 28.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int IsolatedSubnetMask
        {
            get { return _IsolatedSubnetMask; }
            set { _IsolatedSubnetMask = Guard.Range(value, 16, 28, nameof(IsolatedSubnetMask)); }
        }

        /// <summary>
        /// Id of the AWS managed prefix list com.amazonaws.global.cloudfront.origin-facing in <see cref="EnvironmentConfig.Region"/>.
        /// Only these addresses may reach the load balancer's CloudFront origin listener. The id differs per region
        /// and cannot be looked up during a no-lookups synth; print it with
        /// aws ec2 describe-managed-prefix-lists --region &lt;region&gt; --filters Name=prefix-list-name,Values=com.amazonaws.global.cloudfront.origin-facing.
        /// Default pl-82a045eb (us-west-2). Override with the cloudFrontPrefixListId context value.
        /// </summary>
        /// <exception cref="ArgumentException">The value is not a prefix list id.</exception>
        public string CloudFrontOriginFacingPrefixListId
        {
            get
            {
                return _CloudFrontOriginFacingPrefixListId;
            }
            set
            {
                string text = (value ?? String.Empty).Trim();
                if (!_PrefixListPattern.IsMatch(text))
                {
                    throw new ArgumentException("CloudFrontOriginFacingPrefixListId must be a prefix list id (pl- followed by 8 to 17 hex digits), got '" + text + "'.", nameof(CloudFrontOriginFacingPrefixListId));
                }

                _CloudFrontOriginFacingPrefixListId = text;
            }
        }

        /// <summary>
        /// IP addresses and CIDR ranges the server trusts as proxies in addition to the VPC
        /// (TK_ADDITIONAL_TRUSTED_PROXIES, setting rest.additionalTrustedProxies, live), normally the IPv4
        /// CLOUDFRONT_ORIGIN_FACING ranges from https://ip-ranges.amazonaws.com/ip-ranges.json. The backend needs
        /// literal ranges (it cannot read a prefix list). Default empty: the variable is not set, so the console value
        /// applies. Set with the additionalTrustedProxies context value (comma-separated); see infra/README.md.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        /// <exception cref="ArgumentException">An entry is not an IP address or CIDR range.</exception>
        public List<string> AdditionalTrustedProxies
        {
            get
            {
                return _AdditionalTrustedProxies;
            }
            set
            {
                List<string> entries = new List<string>();
                foreach (string entry in Guard.NotNull(value, nameof(AdditionalTrustedProxies)))
                {
                    string text = (entry ?? String.Empty).Trim();
                    if (text.Length == 0) continue;
                    if (!IPNetwork.TryParse(text, out IPNetwork _) && !IPAddress.TryParse(text, out IPAddress? _))
                    {
                        throw new ArgumentException("'" + text + "' in AdditionalTrustedProxies is not an IP address or CIDR range.", nameof(AdditionalTrustedProxies));
                    }

                    if (!entries.Contains(text)) entries.Add(text);
                }

                _AdditionalTrustedProxies = entries;
            }
        }
    }
}
