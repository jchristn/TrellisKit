namespace TrellisKit.Infra.Stacks
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Amazon.CDK;
    using Amazon.CDK.AWS.CertificateManager;
    using Amazon.CDK.AWS.IAM;
    using Amazon.CDK.AWS.Lambda;
    using Amazon.CDK.AWS.Logs;
    using TrellisKit.Infra.Config;
    using Constructs;

    /// <summary>
    /// External DNS mode: a custom resource (an inline Node.js function, Custom::CertificateIssuedCheck) that waits up to
    /// DnsSettings.CertificateWaitSeconds for certificates to be issued before the resources that use them (load
    /// balancer listeners, CloudFront distributions) are created. When a certificate is still pending, the deployment
    /// fails with a message that names the validation records still missing and where their outputs are, instead of
    /// an ELB or CloudFront error about an invalid certificate. Certificates may live in another region (us-east-1 for
    /// CloudFront); the region is read from each ARN.
    /// </summary>
    public class CertificateIssuedCheck : Construct
    {
        /// <summary>
        /// Custom resource type of the check.
        /// </summary>
        public static readonly string ResourceType = "Custom::CertificateIssuedCheck";

        /// <summary>
        /// The custom resource. Make the resources that use the certificates depend on it.
        /// </summary>
        public CustomResource Check { get; }

        /// <summary>
        /// Instantiate the construct.
        /// </summary>
        /// <param name="scope">Parent construct (the stack).</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Environment configuration.</param>
        /// <param name="logPath">Suffix of the function's log group path, for example certificate-check-web.</param>
        /// <param name="certificates">Certificates that must be issued.</param>
        /// <param name="hint">What the operator should do when a certificate is still pending.</param>
        /// <exception cref="ArgumentNullException">A required argument is null or empty.</exception>
        /// <exception cref="ArgumentException">No certificates are given.</exception>
        public CertificateIssuedCheck(
            Construct scope,
            string id,
            EnvironmentConfig config,
            string logPath,
            IReadOnlyList<ICertificate> certificates,
            string hint) : base(scope, id)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(certificates);
            if (String.IsNullOrWhiteSpace(logPath)) throw new ArgumentNullException(nameof(logPath));
            if (String.IsNullOrWhiteSpace(hint)) throw new ArgumentNullException(nameof(hint));
            if (certificates.Count == 0) throw new ArgumentException("The check needs at least one certificate.", nameof(certificates));

            List<string> arns = new List<string>();
            foreach (ICertificate certificate in certificates) arns.Add(certificate.CertificateArn);

            LogGroup logs = new LogGroup(this, "Logs", new LogGroupProps
            {
                LogGroupName = "/" + config.ResourcePath(logPath),
                Retention = config.Observability.LogRetention,
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            Role role = new Role(this, "Role", new RoleProps
            {
                AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
                Description = "Waits for ACM certificates to be issued"
            });
            role.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "DescribeCertificates",
                Actions = new string[] { "acm:DescribeCertificate" },
                Resources = arns.ToArray()
            }));
            role.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "Logs",
                Actions = new string[] { "logs:CreateLogStream", "logs:PutLogEvents" },
                Resources = new string[] { logs.LogGroupArn }
            }));

            Function function = new Function(this, "Function", new FunctionProps
            {
                Description = "Waits for ACM certificates to be issued and explains which validation records are missing",
                Runtime = Runtime.NODEJS_26_X,
                Handler = "index.handler",
                Code = Code.FromInline(BuildHandler()),
                Role = role,
                LogGroup = logs,
                Timeout = Duration.Seconds(config.Dns.CertificateWaitSeconds + 60),
                MemorySize = 128
            });

            Check = new CustomResource(this, "Check", new CustomResourceProps
            {
                ServiceToken = function.FunctionArn,
                ResourceType = ResourceType,
                Properties = new Dictionary<string, object>
                {
                    ["CertificateArns"] = arns.ToArray(),
                    ["WaitSeconds"] = config.Dns.CertificateWaitSeconds.ToString(CultureInfo.InvariantCulture),
                    ["Hint"] = hint
                }
            });
            Check.Node.AddDependency(role);
        }

        /// <summary>
        /// Make every other construct of the stack depend on the check, so nothing is created (and, on a failed first
        /// deployment, nothing retained by a removal policy is left behind under a fixed name) before the certificates
        /// are issued. Call at the end of the stack's constructor.
        /// </summary>
        /// <param name="stack">Stack that owns the check.</param>
        /// <exception cref="ArgumentNullException">The stack is null.</exception>
        public void Gate(Stack stack)
        {
            ArgumentNullException.ThrowIfNull(stack);
            foreach (IConstruct child in stack.Node.Children)
            {
                if (ReferenceEquals(child, this) || (child is CfnElement && !(child is CfnResource))) continue;
                child.Node.AddDependency(this);
            }
        }

        /// <summary>
        /// The function source (Node.js, AWS SDK v3 from the runtime).
        /// </summary>
        /// <returns>JavaScript source.</returns>
        public static string BuildHandler()
        {
            StringBuilder js = new StringBuilder();
            js.Append("const { ACMClient, DescribeCertificateCommand } = require('@aws-sdk/client-acm');\n");
            js.Append("const https = require('https');\n");
            js.Append("const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));\n");
            js.Append("function respond(event, status, reason, physicalId) {\n");
            js.Append("  const body = JSON.stringify({ Status: status, Reason: String(reason).substring(0, 900), PhysicalResourceId: physicalId,\n");
            js.Append("    StackId: event.StackId, RequestId: event.RequestId, LogicalResourceId: event.LogicalResourceId, Data: {} });\n");
            js.Append("  const url = new URL(event.ResponseURL);\n");
            js.Append("  return new Promise((resolve) => {\n");
            js.Append("    const request = https.request({ hostname: url.hostname, path: url.pathname + url.search, method: 'PUT',\n");
            js.Append("      headers: { 'content-type': '', 'content-length': Buffer.byteLength(body) } }, (response) => { response.resume(); response.on('end', resolve); });\n");
            js.Append("    request.on('error', resolve);\n");
            js.Append("    request.end(body);\n");
            js.Append("  });\n");
            js.Append("}\n");
            js.Append("exports.handler = async (event) => {\n");
            js.Append("  const physicalId = event.PhysicalResourceId || ('certificate-check-' + event.LogicalResourceId);\n");
            js.Append("  if (event.RequestType === 'Delete') { await respond(event, 'SUCCESS', 'OK', physicalId); return; }\n");
            js.Append("  const props = event.ResourceProperties;\n");
            js.Append("  const deadline = Date.now() + Number(props.WaitSeconds) * 1000;\n");
            js.Append("  try {\n");
            js.Append("    for (;;) {\n");
            js.Append("      const pending = [];\n");
            js.Append("      for (const arn of props.CertificateArns) {\n");
            js.Append("        const client = new ACMClient({ region: arn.split(':')[3] });\n");
            js.Append("        const certificate = (await client.send(new DescribeCertificateCommand({ CertificateArn: arn }))).Certificate;\n");
            js.Append("        if (certificate.Status === 'ISSUED') continue;\n");
            js.Append("        if (certificate.Status !== 'PENDING_VALIDATION') throw new Error('The certificate for ' + certificate.DomainName + ' is ' + certificate.Status + '. ' + props.Hint);\n");
            js.Append("        const missing = (certificate.DomainValidationOptions || []).filter((o) => o.ResourceRecord && o.ValidationStatus !== 'SUCCESS')\n");
            js.Append("          .map((o) => o.ResourceRecord.Name + ' CNAME ' + o.ResourceRecord.Value);\n");
            js.Append("        pending.push(certificate.DomainName + ' (' + missing.join('; ') + ')');\n");
            js.Append("      }\n");
            js.Append("      if (pending.length === 0) { await respond(event, 'SUCCESS', 'Every certificate is issued', physicalId); return; }\n");
            js.Append("      if (Date.now() > deadline) {\n");
            js.Append("        await respond(event, 'FAILED', 'Certificate not issued yet. ' + props.Hint + ' Pending: ' + pending.join(', '), physicalId);\n");
            js.Append("        return;\n");
            js.Append("      }\n");
            js.Append("      console.log('Waiting for ' + pending.join(', '));\n");
            js.Append("      await sleep(15000);\n");
            js.Append("    }\n");
            js.Append("  } catch (e) {\n");
            js.Append("    console.log('Certificate check failed: ' + e.name + ': ' + e.message);\n");
            js.Append("    await respond(event, 'FAILED', e.message, physicalId);\n");
            js.Append("  }\n");
            js.Append("};\n");
            return js.ToString();
        }
    }
}
