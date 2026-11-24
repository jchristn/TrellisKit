namespace TrellisKit.Infra.Stacks
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Amazon.CDK;
    using Amazon.CDK.AWS.IAM;
    using Amazon.CDK.AWS.Lambda;
    using Amazon.CDK.AWS.Logs;
    using Amazon.CDK.AWS.SecretsManager;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Nag;
    using Constructs;
    using SecretsManagerSecret = Amazon.CDK.AWS.SecretsManager.Secret;

    /// <summary>
    /// The engine secrets key: the secret {prefix}/secrets-key with TK_SECRETS_ENCRYPTION_KEY and
    /// TK_SECRETS_PREVIOUS_ENCRYPTION_KEYS, plus a one-shot custom resource that writes a random base64 key of 32 bytes
    /// into it when the key is empty. Secrets Manager's own generator cannot produce base64 of exactly 32 bytes (the
    /// backend's format), hence the function. The function only ever fills an empty key: a key that is already set
    /// (generated earlier or put by an operator) is never replaced, so no deploy rotates it. The template value of the
    /// secret is a constant with empty keys and must never change, because a changed template value would make
    /// CloudFormation overwrite the stored key. Rotation is manual (infra/README.md).
    /// </summary>
    public class SecretsKeySecret : Construct
    {
        /// <summary>
        /// The template value of the secret. Never change it: CloudFormation would write it over the generated key.
        /// </summary>
        public static readonly string TemplateValue = "{\"" + ServerEnvironmentKeys.SecretsEncryptionKey + "\":\"\",\"" + ServerEnvironmentKeys.SecretsPreviousEncryptionKeys + "\":\"\"}";

        /// <summary>
        /// The secret.
        /// </summary>
        public SecretsManagerSecret Secret { get; }

        /// <summary>
        /// The custom resource that fills the key; the ECS service depends on it.
        /// </summary>
        public CustomResource Initializer { get; }

        /// <summary>
        /// The function behind <see cref="Initializer"/>.
        /// </summary>
        public Function Function { get; }

        /// <summary>
        /// Instantiate the construct.
        /// </summary>
        /// <param name="scope">Parent construct.</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Environment configuration.</param>
        /// <exception cref="ArgumentNullException">The configuration is null.</exception>
        public SecretsKeySecret(Construct scope, string id, EnvironmentConfig config) : base(scope, id)
        {
            ArgumentNullException.ThrowIfNull(config);

            Secret = new SecretsManagerSecret(this, "Secret", new SecretProps
            {
                SecretName = config.ResourcePath("secrets-key"),
                Description = config.DisplayName + " engine secrets key (" + ServerEnvironmentKeys.SecretsEncryptionKey + ", generated once and never rotated by a deploy) and "
                    + ServerEnvironmentKeys.SecretsPreviousEncryptionKeys + " (earlier keys after a rotation). See infra/README.md.",
                SecretObjectValue = new Dictionary<string, SecretValue>
                {
                    [ServerEnvironmentKeys.SecretsEncryptionKey] = SecretValue.UnsafePlainText(String.Empty),
                    [ServerEnvironmentKeys.SecretsPreviousEncryptionKeys] = SecretValue.UnsafePlainText(String.Empty)
                },
                RemovalPolicy = config.StatefulRemovalPolicy
            });
            NagHelper.Suppress(Secret, "AwsSolutions-SMG4",
                "The key encrypts stored integration secrets and queued email; rotating it needs the previous key listed until every value is rewritten, so it is rotated manually (see infra/README.md).");

            LogGroup logs = new LogGroup(this, "Logs", new LogGroupProps
            {
                LogGroupName = "/" + config.ResourcePath("secrets-key-init"),
                Retention = config.Observability.LogRetention,
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            Role role = new Role(this, "Role", new RoleProps
            {
                AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
                Description = "Fills an empty engine secrets key once"
            });
            role.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "SecretsKeyReadWrite",
                Actions = new string[] { "secretsmanager:GetSecretValue", "secretsmanager:PutSecretValue" },
                Resources = new string[] { Secret.SecretArn }
            }));
            role.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "Logs",
                Actions = new string[] { "logs:CreateLogStream", "logs:PutLogEvents" },
                Resources = new string[] { logs.LogGroupArn }
            }));

            Function = new Function(this, "Function", new FunctionProps
            {
                Description = "Writes a random base64 32-byte key into an empty engine secrets key; never replaces a set key",
                Runtime = Runtime.NODEJS_26_X,
                Handler = "index.handler",
                Code = Code.FromInline(BuildHandler()),
                Role = role,
                LogGroup = logs,
                Timeout = Duration.Seconds(60),
                MemorySize = 128
            });

            Initializer = new CustomResource(this, "Initializer", new CustomResourceProps
            {
                ServiceToken = Function.FunctionArn,
                ResourceType = "Custom::SecretsKeyInitializer",
                Properties = new Dictionary<string, object>
                {
                    ["SecretArn"] = Secret.SecretArn
                }
            });
            Initializer.Node.AddDependency(role);
        }

        /// <summary>
        /// The function source (Node.js, AWS SDK v3 from the runtime). It never logs or returns the key.
        /// </summary>
        /// <returns>JavaScript source.</returns>
        public static string BuildHandler()
        {
            StringBuilder js = new StringBuilder();
            js.Append("const { SecretsManagerClient, GetSecretValueCommand, PutSecretValueCommand } = require('@aws-sdk/client-secrets-manager');\n");
            js.Append("const crypto = require('crypto');\n");
            js.Append("const https = require('https');\n");
            js.Append("const KEY = '" + ServerEnvironmentKeys.SecretsEncryptionKey + "';\n");
            js.Append("const PREVIOUS = '" + ServerEnvironmentKeys.SecretsPreviousEncryptionKeys + "';\n");
            js.Append("function respond(event, status, reason) {\n");
            js.Append("  const body = JSON.stringify({ Status: status, Reason: reason, PhysicalResourceId: event.PhysicalResourceId || event.ResourceProperties.SecretArn,\n");
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
            js.Append("  try {\n");
            js.Append("    if (event.RequestType !== 'Delete') {\n");
            js.Append("      const client = new SecretsManagerClient({});\n");
            js.Append("      const arn = event.ResourceProperties.SecretArn;\n");
            js.Append("      let current = {};\n");
            js.Append("      try {\n");
            js.Append("        const value = await client.send(new GetSecretValueCommand({ SecretId: arn }));\n");
            js.Append("        if (value.SecretString) current = JSON.parse(value.SecretString);\n");
            js.Append("      } catch (e) {\n");
            js.Append("        if (e.name !== 'ResourceNotFoundException') throw e;\n");
            js.Append("      }\n");
            js.Append("      if (typeof current[KEY] === 'string' && current[KEY].trim().length > 0) {\n");
            js.Append("        console.log('The secrets key is already set; left unchanged.');\n");
            js.Append("      } else {\n");
            js.Append("        current[KEY] = crypto.randomBytes(32).toString('base64');\n");
            js.Append("        if (typeof current[PREVIOUS] !== 'string') current[PREVIOUS] = '';\n");
            js.Append("        await client.send(new PutSecretValueCommand({ SecretId: arn, SecretString: JSON.stringify(current) }));\n");
            js.Append("        console.log('Generated the secrets key.');\n");
            js.Append("      }\n");
            js.Append("    }\n");
            js.Append("    await respond(event, 'SUCCESS', 'OK');\n");
            js.Append("  } catch (e) {\n");
            js.Append("    console.log('Secrets key initialization failed: ' + e.name);\n");
            js.Append("    await respond(event, 'FAILED', 'Secrets key initialization failed: ' + e.name);\n");
            js.Append("  }\n");
            js.Append("};\n");
            return js.ToString();
        }
    }
}
