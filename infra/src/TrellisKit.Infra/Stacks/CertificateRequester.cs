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
    using TrellisKit.Infra.Nag;
    using Constructs;

    /// <summary>
    /// Requests DNS-validated ACM certificates in external DNS mode without waiting for validation. A native
    /// AWS::CertificateManager::Certificate holds its stack in CREATE_IN_PROGRESS until the validation record exists,
    /// and its validation records are not available as attributes, so they could never be stack outputs. Instead a
    /// custom resource (an inline Node.js function, Custom::DnsValidatedCertificateRequest) requests the certificate,
    /// waits only until ACM publishes the validation records (seconds), and returns them, so the certificate stack
    /// completes at once with DnsValidation* outputs. Issuance happens once the operator creates those CNAME records;
    /// stacks that use a certificate wait for it with <see cref="CertificateIssuedCheck"/>. Deleting the resource
    /// deletes the certificate (one still attached elsewhere is left in place and logged).
    /// </summary>
    public class CertificateRequester : Construct
    {
        /// <summary>
        /// Custom resource type of each request.
        /// </summary>
        public static readonly string ResourceType = "Custom::DnsValidatedCertificateRequest";

        private readonly EnvironmentConfig _Config;

        /// <summary>
        /// The function that serves every request of the stack.
        /// </summary>
        public Function Function { get; }

        /// <summary>
        /// Certificates requested so far.
        /// </summary>
        public List<ExternalCertificate> Certificates { get; } = new List<ExternalCertificate>();

        /// <summary>
        /// Instantiate the construct (one per stack).
        /// </summary>
        /// <param name="scope">Parent construct (the stack).</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Environment configuration.</param>
        /// <param name="logPath">Suffix of the function's log group path, for example certificate-requests.</param>
        /// <exception cref="ArgumentNullException">A required argument is null or empty.</exception>
        public CertificateRequester(Construct scope, string id, EnvironmentConfig config, string logPath) : base(scope, id)
        {
            _Config = config ?? throw new ArgumentNullException(nameof(config));
            if (String.IsNullOrWhiteSpace(logPath)) throw new ArgumentNullException(nameof(logPath));

            LogGroup logs = new LogGroup(this, "Logs", new LogGroupProps
            {
                LogGroupName = "/" + config.ResourcePath(logPath),
                Retention = config.Observability.LogRetention,
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            Role role = new Role(this, "Role", new RoleProps
            {
                AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
                Description = "Requests DNS-validated ACM certificates and reports their validation records"
            });
            role.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "RequestCertificates",
                Actions = new string[] { "acm:RequestCertificate" },
                Resources = new string[] { "*" }
            }));
            role.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "ManageCertificates",
                Actions = new string[] { "acm:DescribeCertificate", "acm:DeleteCertificate", "acm:AddTagsToCertificate" },
                Resources = new string[] { "arn:" + Aws.PARTITION + ":acm:" + Aws.REGION + ":" + Aws.ACCOUNT_ID + ":certificate/*" }
            }));
            role.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "Logs",
                Actions = new string[] { "logs:CreateLogStream", "logs:PutLogEvents" },
                Resources = new string[] { logs.LogGroupArn }
            }));
            NagHelper.SuppressWildcards(role,
                "acm:RequestCertificate has no resource-level permissions, and the certificate ARN is assigned by ACM at request time, so describe, tag, and delete are limited to the certificates of this account and region.",
                NagHelper.AnyResourcePattern,
                "/^Resource::arn:<AWS::Partition>:acm:<AWS::Region>:<AWS::AccountId>:certificate\\/\\*$/g");

            Function = new Function(this, "Function", new FunctionProps
            {
                Description = "Requests DNS-validated ACM certificates and returns their validation records without waiting for issuance",
                Runtime = Runtime.NODEJS_26_X,
                Handler = "index.handler",
                Code = Code.FromInline(BuildHandler()),
                Role = role,
                LogGroup = logs,
                Timeout = Duration.Seconds(config.Dns.ValidationRecordWaitSeconds + 60),
                MemorySize = 128
            });
        }

        /// <summary>
        /// Request a certificate and add its outputs to the stack: {id}Arn and one DnsValidation{id}{index} record per
        /// host name ("name CNAME value").
        /// </summary>
        /// <param name="id">Construct id and output key part, for example Api.</param>
        /// <param name="domainNames">Host names (the first is the domain name, the rest subject alternative names).</param>
        /// <returns>The certificate.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null or empty.</exception>
        /// <exception cref="ArgumentException">No host names are given.</exception>
        public ExternalCertificate Request(string id, IReadOnlyList<string> domainNames)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            ArgumentNullException.ThrowIfNull(domainNames);
            if (domainNames.Count == 0) throw new ArgumentException("A certificate needs at least one host name.", nameof(domainNames));

            List<string> names = new List<string>(domainNames);
            CustomResource request = new CustomResource(this, id + "Certificate", new CustomResourceProps
            {
                ServiceToken = Function.FunctionArn,
                ResourceType = ResourceType,
                Properties = new Dictionary<string, object>
                {
                    ["DomainNames"] = names.ToArray(),
                    ["Name"] = _Config.ResourceName(id.ToLowerInvariant()),
                    ["RecordWaitSeconds"] = _Config.Dns.ValidationRecordWaitSeconds.ToString(CultureInfo.InvariantCulture)
                }
            });
            request.Node.AddDependency(Function.Role!);

            ICertificate certificate = Certificate.FromCertificateArn(this, id + "CertificateRef", request.Ref);
            Stack stack = Stack.Of(this);
            new CfnOutput(stack, id + "CertificateArn", new CfnOutputProps
            {
                Value = request.Ref,
                Description = "Certificate for " + String.Join(", ", names) + ". Wait for it with: aws acm wait certificate-validated --region "
                    + stack.Region + " --certificate-arn <this value>"
            });
            for (int i = 0; i < names.Count; i++)
            {
                string index = i.ToString(CultureInfo.InvariantCulture);
                new CfnOutput(stack, DnsRecordOutputs.ValidationPrefix + id + index, new CfnOutputProps
                {
                    Value = Fn.Join(String.Empty, new string[]
                    {
                        request.GetAttString("ValidationName" + index), " CNAME ", request.GetAttString("ValidationValue" + index)
                    }),
                    Description = "Create at your DNS provider so ACM can issue the certificate for " + names[i] + " (keep it: ACM renews with it)."
                });
            }

            ExternalCertificate result = new ExternalCertificate(certificate, request, names);
            Certificates.Add(result);
            return result;
        }

        /// <summary>
        /// The function source (Node.js, AWS SDK v3 from the runtime).
        /// </summary>
        /// <returns>JavaScript source.</returns>
        public static string BuildHandler()
        {
            StringBuilder js = new StringBuilder();
            js.Append("const { ACMClient, RequestCertificateCommand, DescribeCertificateCommand, DeleteCertificateCommand } = require('@aws-sdk/client-acm');\n");
            js.Append("const https = require('https');\n");
            js.Append("const client = new ACMClient({});\n");
            js.Append("const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));\n");
            js.Append("function respond(event, status, reason, physicalId, data) {\n");
            js.Append("  const body = JSON.stringify({ Status: status, Reason: String(reason).substring(0, 900), PhysicalResourceId: physicalId,\n");
            js.Append("    StackId: event.StackId, RequestId: event.RequestId, LogicalResourceId: event.LogicalResourceId, Data: data || {} });\n");
            js.Append("  const url = new URL(event.ResponseURL);\n");
            js.Append("  return new Promise((resolve) => {\n");
            js.Append("    const request = https.request({ hostname: url.hostname, path: url.pathname + url.search, method: 'PUT',\n");
            js.Append("      headers: { 'content-type': '', 'content-length': Buffer.byteLength(body) } }, (response) => { response.resume(); response.on('end', resolve); });\n");
            js.Append("    request.on('error', resolve);\n");
            js.Append("    request.end(body);\n");
            js.Append("  });\n");
            js.Append("}\n");
            js.Append("async function records(arn, domains, waitSeconds) {\n");
            js.Append("  const deadline = Date.now() + waitSeconds * 1000;\n");
            js.Append("  for (;;) {\n");
            js.Append("    const certificate = (await client.send(new DescribeCertificateCommand({ CertificateArn: arn }))).Certificate;\n");
            js.Append("    const options = certificate.DomainValidationOptions || [];\n");
            js.Append("    const data = { Status: certificate.Status };\n");
            js.Append("    let complete = true;\n");
            js.Append("    domains.forEach((domain, i) => {\n");
            js.Append("      const option = options.find((o) => o.DomainName === domain);\n");
            js.Append("      if (option && option.ResourceRecord) { data['ValidationName' + i] = option.ResourceRecord.Name; data['ValidationValue' + i] = option.ResourceRecord.Value; }\n");
            js.Append("      else { complete = false; }\n");
            js.Append("    });\n");
            js.Append("    if (complete) return data;\n");
            js.Append("    if (Date.now() > deadline) throw new Error('ACM did not publish the validation records of ' + domains.join(', ') + ' in time; deploy again.');\n");
            js.Append("    await sleep(5000);\n");
            js.Append("  }\n");
            js.Append("}\n");
            js.Append("exports.handler = async (event) => {\n");
            js.Append("  const props = event.ResourceProperties;\n");
            js.Append("  const domains = props.DomainNames;\n");
            js.Append("  let physicalId = event.PhysicalResourceId || ('certificate-request-' + event.RequestId);\n");
            js.Append("  try {\n");
            js.Append("    if (event.RequestType === 'Delete') {\n");
            js.Append("      if (physicalId.startsWith('arn:')) {\n");
            js.Append("        try { await client.send(new DeleteCertificateCommand({ CertificateArn: physicalId })); }\n");
            js.Append("        catch (e) {\n");
            js.Append("          if (e.name !== 'ResourceInUseException' && e.name !== 'ResourceNotFoundException') throw e;\n");
            js.Append("          console.log('Certificate ' + physicalId + ' left in place: ' + e.name);\n");
            js.Append("        }\n");
            js.Append("      }\n");
            js.Append("      await respond(event, 'SUCCESS', 'OK', physicalId, {});\n");
            js.Append("      return;\n");
            js.Append("    }\n");
            js.Append("    const old = event.OldResourceProperties;\n");
            js.Append("    const same = event.RequestType === 'Update' && old && JSON.stringify(old.DomainNames) === JSON.stringify(domains) && physicalId.startsWith('arn:');\n");
            js.Append("    if (!same) {\n");
            js.Append("      const request = { DomainName: domains[0], ValidationMethod: 'DNS', IdempotencyToken: event.RequestId.replace(/[^A-Za-z0-9]/g, '').substring(0, 32),\n");
            js.Append("        Tags: [{ Key: 'Name', Value: props.Name }] };\n");
            js.Append("      if (domains.length > 1) request.SubjectAlternativeNames = domains.slice(1);\n");
            js.Append("      physicalId = (await client.send(new RequestCertificateCommand(request))).CertificateArn;\n");
            js.Append("      console.log('Requested ' + physicalId + ' for ' + domains.join(', '));\n");
            js.Append("    }\n");
            js.Append("    const data = await records(physicalId, domains, Number(props.RecordWaitSeconds));\n");
            js.Append("    await respond(event, 'SUCCESS', 'OK', physicalId, data);\n");
            js.Append("  } catch (e) {\n");
            js.Append("    console.log('Certificate request failed: ' + e.name + ': ' + e.message);\n");
            js.Append("    await respond(event, 'FAILED', 'Certificate request failed: ' + e.message, event.PhysicalResourceId || physicalId, {});\n");
            js.Append("  }\n");
            js.Append("};\n");
            return js.ToString();
        }
    }
}
