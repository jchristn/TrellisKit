# Infrastructure

AWS infrastructure for TrellisKit properties as code: AWS CDK v2 in C# (.NET 10). The engine code names no property; every name, domain, and address comes from the property selected with the CDK context value `propertyDir` (default `samples/example`, the neutral sample), or from the properties listed in a deployment file (`-c deployment=<file>`, see "Deployment topology"). See `docs/TRELLISKIT.md`.

Nothing here deploys on its own. CI only builds, tests, and synthesizes without AWS credentials: `.github/workflows/infra.yml` for the engine (sample property) and each property's own workflow for that property.

## The property

`-c propertyDir=<folder>` selects the property: an absolute path, or a path relative to the engine root (the folder that contains `infra/cdk.json`). The app reads:

| From `property.json` | Used for |
|---|---|
| `id` | The opaque generated property id (`app_{k-sortable}_{random}`, pattern `^app_[a-z0-9]{6,16}_[A-Za-z0-9]{8,40}$`). It has underscores and uppercase letters, which CloudFormation, S3, ECR, and RDS names disallow, so it never names a resource. It is what the server and the data use (the server reads it from the baked `property.json`; media and updates storage keys are prefixed with it by the backend) and it is set as the `PropertyId` tag on every property stack |
| `slug` | Cosmetic name (`^[a-z0-9][a-z0-9-]{0,62}$`, at most 20 characters here because physical names such as the 32-character load balancer name append the environment and a purpose). Optional: when absent it is derived from `shortName` (else `name`) exactly as the backend's `EffectiveSlug` and `shared/src/property.js` `propertySlug` do. Names stacks `<slug>-<env>-<Name>` (for example `example-prod-Api`), the resource prefix `<slug>-<env>`, secret paths `<slug>/<env>/...`, the `Project` tag, and `TK_DEPLOYMENT_NAME` |
| `name` | Stack, zone, alarm topic, and secret descriptions |
| `domains.web`, `domains.api`, `domains.media` | Primary zone and public API/media hosts. API may equal `web`; otherwise API and media must be subdomains of `web`. Development uses a `dev.` web zone. |
| `domains.redirects` | One hosted zone per vanity domain and the Redirect stack. With none, no Redirect stack is created |
| `contact.supportEmail` | Default alarm and budget address |
| `contact.emailFromAddress` | Local part of the deployment sender (SES or Mailgun) on each environment's primary domain (`TK_SMTP_FROM_ADDRESS`) |
| `mobile.bundleId`, `mobile.androidPackage`, `membership.storeProductId` | Public store identifiers passed as plain environment variables (`TK_APPSTORE_BUNDLE_ID`, `TK_APPLE_BUNDLE_ID`, `TK_GOOGLEPLAY_PACKAGE_NAME`, `TK_APPSTORE_PRODUCT_ID`, `TK_GOOGLEPLAY_PRODUCT_ID`) |

Property-owned deployment values live in the property's optional `deploy/infra.json`, never in engine files:

```json
{
  "accounts": { "dev": "111111111111", "prod": "222222222222" },
  "alarmEmail": "alerts@example.test",
  "github": { "owner": "<github owner>", "repository": "<repository>" },
  "mail": { "provider": "Mailgun", "mailgunDomain": "mg.example.test", "mailgunBaseUrl": "https://api.eu.mailgun.net/v3/" },
  "dns": { "mode": "Route53" },
  "location": { "placeIndex": true, "dataProvider": "Esri", "map": false },
  "partio": { "baseUrl": "https://partio.example.test" }
}
```

`accounts.dev` and `accounts.prod` may be the same account id (see "One account for dev and prod").

When `domains.api` equals `domains.web`, the public API stays on the website host and CloudFront routes `/api/*` to a dedicated load-balancer origin. Its default hostname is `api-origin.<web-domain>` in production and `api-origin.dev.<web-domain>` in development. Optional `deploy/infra.json` `apiOriginDomain` overrides the production origin hostname, for example `backend.example.test`; development uses `backend.dev.example.test`. This must be a subdomain of the web domain, distinct from the website, `www`, and media hosts in both production and development. When the public API already has a separate host, that host remains the origin and a different override is rejected. API DNS records, TLS certificates, and external-DNS outputs use the origin hostname; public API URLs, property configuration, CORS, and authentication URLs remain unchanged. No property-specific engine code or configuration change is required for the default.

`dns` (optional) selects who manages DNS. `mode` is `Route53` (the default: the stacks create hosted zones, every record, and zone-validated certificates; the operator only delegates nameservers) or `External` (no hosted zones and no Route 53 records; certificates are requested with DNS validation and every record the operator must create, validation records included, is a stack output; see "Managing DNS yourself"). `certificateWaitSeconds` (default 600, 0 to 840) is how long, in `External` mode, a stack that uses a certificate waits for it to be issued before it fails with an explanation. A deployment file's `dns` section wins over each property's, and the `dnsMode` context value wins over both. Route 53 stays the default because it needs no manual record keeping; `External` suits domains whose DNS stays at a registrar or another provider.

`mail` (optional, every key optional) is the deployment-level mail of the backend: platform administrator mail and, in `SingleProperty`, the property's default (its own email settings in the admin portal win). It also decides whether Amazon SES is provisioned:

| `provider` | `TK_EMAIL_PROVIDER` | Email stack (SES identity, DKIM, MAIL FROM MX and SPF, DMARC) and `TK_SMTP_HOST`, `TK_SMTP_PORT`, `TK_SMTP_USE_TLS` (SES SMTP endpoint) |
|---|---|---|
| unset (the engine default, like the backend's `Auto` with the SES SMTP host set) | not set | Created |
| `Auto`, `Smtp`, `Log` | the same value | Created |
| `Ses` | `Smtp` | Created |
| `Mailgun` | `Mailgun` | Not created: no SES identity, and no SES DKIM, MX, or SPF records are created or emitted |
| `None` | not set (the backend default and the console apply) | Not created |

`mailgunDomain` becomes `TK_MAILGUN_DOMAIN`, `mailgunBaseUrl` (`https://api.mailgun.net/v3/` US, `https://api.eu.mailgun.net/v3/` EU) `TK_MAILGUN_BASE_URL`. These are live settings, so each is passed only when set (without `mailgunDomain`, enter the sending domain in the admin console). When `mailgunDomain` is set or `provider` is `Mailgun`, the Api stack also creates `<slug>/<env>/mailgun` and injects its `TK_MAILGUN_API_KEY` (filled by hand, never in a file or context value). With `Mailgun` the Api stack output `MailDnsRecords` names the records to create from the Mailgun dashboard (see "Managing DNS yourself"). In the shared topology the deployment file's `mail` section applies instead (it decides every property's Email stack) and properties' `mail` sections are ignored.

`location` (optional, off by default) provisions Amazon Location Service for the directory (`docs/PHASE4_CONTRACT.md` section 6): `placeIndex` (boolean) creates the place index `<slug>-<env>-places` (`dataProvider` `Esri` (default), `Here`, or `Grab`; `intendedUse` `Storage` (default, the geocoder caches answers and stores coordinates) or `SingleUse`) and grants the task role `geo:SearchPlaceIndexForText` on it; `map` (boolean) creates the map `<slug>-<env>-map` (`mapStyle`, default `VectorEsriNavigation`) and grants `geo:GetMapGlyphs`, `geo:GetMapSprites`, `geo:GetMapStyleDescriptor`, and `geo:GetMapTile` on it only. Without the section there is no Location resource and no `geo:` permission, so it costs nothing; with it, cost is per request only (no fixed fee). The stacks never set the live directory settings; see manual step 17. In the shared topology each property's own section creates that property's resources in the shared Api stack (output names carry the property suffix).

`partio` (optional) points the document text pipeline at Partio: `baseUrl` (absolute https) becomes `TK_PARTIO_BASE_URL`, and the Api stack creates `<slug>/<env>/partio`, injected as `TK_PARTIO_API_KEY` (filled by hand). Both are live settings, so nothing is passed and no secret exists unless `baseUrl` is set; turning Partio on (`documents.partio.enabled`) stays a console setting. In the shared topology the first listed property's section applies.

The app validates the property at synth time (generated id format, slug format and length, required fields, API and media hosts under the web domain, redirect domains outside it, twelve-digit accounts) and stops with a message naming the problem.

## Layout

```text
infra/
|-- cdk.json                              # CDK app entry (no property or account values)
|-- src/
    |-- TrellisKit.Infra.sln
    |-- Directory.Build.props             # net10.0, nullable, warnings as errors, XML docs
    |-- TrellisKit.Infra/
    |   |-- Program.cs                    # entry point: -c env=dev|prod [-c propertyDir=<folder> | -c deployment=<file>]
    |   |-- InfraAppBuilder.cs            # adds every stack of a deployment (either topology), tags, cdk-nag
    |   |-- AppStacks.cs, SharedStacks.cs, PropertyStacks.cs, DeploymentStacks.cs   # stack sets per topology
    |   |-- CdkContextDefaults.cs         # feature flags and AZ context (no lookups)
    |   |-- Config/                       # PropertyDefinition, DeploymentDefinition, EnvironmentConfig + one settings class per area, dev and prod factory
    |   |-- Stacks/                       # one stack per file
    |   |-- Nag/NagHelper.cs              # cdk-nag suppressions with reasons
    |   |-- Nag/AwsSolutionsNagPlugin.cs  # cdk-nag 3 validation plugin applying rule-wide and pattern suppressions
    |   |-- Nag/NagSuppression.cs         # one recorded suppression
    |   |-- Nag/NagSuppressionIndex.cs    # recorded suppressions by construct path
    |   |-- Nag/NagSuppressionMetadataAspect.cs # writes suppressions into template metadata
    |-- TrellisKit.Infra.Tests/           # xUnit v3 + Amazon.CDK.Assertions; sample property plus a test-local
                                          # copy with a vanity domain and deploy/infra.json
```

Also `infra/fixtures/shared/`: a shared topology deployment file (the sample property plus a second fixture property with a vanity domain, and one platform host) used by the tests and CI.

Pinned versions: `Amazon.CDK.Lib` 2.277.0, `Constructs` 10.8.1, `Cdklabs.CdkNag` 3.0.2, CDK CLI `aws-cdk@2.1147.0`, ADOT collector `v0.50.0`, database bootstrap image `public.ecr.aws/docker/library/postgres:17-alpine` (`DatabaseSettings.BootstrapImage`).

Two CDK (not cdk-nag) warnings are acknowledged in code: OAC on an imported bucket (media and per-property-topology website distributions), and the access log grant for website buckets owned by property Web stacks in the shared topology (the Storage stack grants log delivery to `s3/sites/*` for every `*-website-<account>` bucket instead).

cdk-nag 3 runs as a CDK policy validation plugin (`Validations.Of(app).AddPlugins`) instead of an aspect, and acknowledges findings only by exact finding id (`AwsSolutions-IAM5[Resource::*]`). `Nag/NagHelper.cs` keeps the cdk-nag 2 semantics: each suppression is acknowledged through `Validations.Of(construct).Acknowledge` and also recorded as construct metadata, `Nag/AwsSolutionsNagPlugin.cs` wraps the AwsSolutions pack and drops findings covered by a rule-wide suppression or a `SuppressWildcards` regular expression, and `Nag/NagSuppressionMetadataAspect.cs` writes every suppression into the template as `cdk_nag.rules_to_suppress` (the cdk-nag 2 format). Unsuppressed errors fail `cdk synth` and `App.Synth()`; the tests assert that no error or warning is left (`SynthesizedApp.NagFindings`) and `NagTests` covers the adapter. Synthesis logs a harmless jsii `ReflectionTypeLoadException` warning about `Cdklabs.CdkNag` `get_StackTemplates` (the package was generated against an older `IPolicyValidationContext`).

## Environments and configuration

All names and sizes come from one typed `EnvironmentConfig` per environment, built in `Config/EnvironmentConfigFactory.cs` from the property. Stacks contain no magic numbers. Every numeric setting validates its range and documents its default, minimum, and maximum.

| | dev | prod |
|---|---|---|
| Account (placeholder unless set; may be the same for both) | `111111111111` | `222222222222` |
| Domains | `dev.<domains.web>`, `dev.<each redirect>` | `<domains.web>`, each redirect |
| NAT gateways | 1 | 2 |
| RDS | db.t4g.micro, single-AZ, 3-day backups, no deletion protection | db.t4g.small, Multi-AZ, 14-day backups, deletion protection |
| Fargate | 0.5 vCPU / 1 GB, 1 to 2 tasks | 1 vCPU / 2 GB, 2 to 6 tasks |
| Log retention | 2 weeks | 3 months |
| Stateful removal policy | destroy (database snapshots) | retain, stack termination protection |
| Budget | 120 USD | 300 USD |

Context values (pass with `-c key=value`):

| Key | Purpose |
|---|---|
| `env` | Required. `dev` or `prod`. |
| `propertyDir` | Property folder (absolute, or relative to the engine root). Default `samples/example`. |
| `account` | Real 12-digit account id. Else `account:<env>` from context, else `accounts.<env>` in the property's `deploy/infra.json`, else the placeholder. |
| `imageTag` | Server image tag in ECR. Default `latest`. |
| `alarmEmail` | Alarm topic and budget recipient. Else `alarmEmail` in `deploy/infra.json`, else the property's `contact.supportEmail`. |
| `mediaSigningPublicKeyPem` | PEM public key for media signed cookies (`\n` escapes allowed). Default is a placeholder. |
| `deployment` | Deployment file (absolute, or relative to the engine root) listing the topology and property folders. Cannot be combined with `propertyDir`. See "Deployment topology". |
| `additionalTrustedProxies` | Comma-separated IP addresses and CIDR ranges passed as `TK_ADDITIONAL_TRUSTED_PROXIES` (live; unset by default), normally the CloudFront origin-facing ranges. See "Client addresses behind CloudFront". |
| `recallDb` | `true` or `false`: run the RecallDB service (overrides `ai.recallDb` in `deploy/infra.json`). Default off. See "AI (RecallDB and the assistant widget)". |
| `embedFrameAncestors` | Comma-separated `https://host[:port]` origins that may frame `/embed/assistants/*` (replaces `ai.embedFrameAncestors`). Default none (`'self'` only). |
| `cloudFrontPrefixListId` | Id of the managed prefix list `com.amazonaws.global.cloudfront.origin-facing` in the primary region. Default `pl-82a045eb` (us-west-2). |
| `dnsMode` | `Route53` or `External` for every property (overrides `dns.mode` in the deployment file and `deploy/infra.json`). |

The optional `ai` section of a property's `deploy/infra.json` (`Config/AiSettings.cs`): `recallDb` (boolean, default false), `recallDbImage` (pinned tag, default `jchristn77/recalldb-server:v0.2.1`), `recallDbCpu` (256), `recallDbMemoryMiB` (1024), `recallDbDesiredCount` (1), `recallDbTenant` (`default`), and `embedFrameAncestors` (array of up to 20 `https://host[:port]` origins). In the shared topology the deployment-wide values (RecallDB) come from the first listed property; each property's website uses its own `embedFrameAncestors`.

Synthesis never reads live AWS state: availability zones are listed in `NetworkSettings` and written into context by `CdkContextDefaults`, hosted zones are created (not looked up) or not used at all (external DNS), and CI runs `synth --no-lookups`.

### One account for dev and prod

dev and prod can deploy into the same account and region (set the same id for `accounts.dev` and `accounts.prod`). Nothing collides, and `SingleAccountTests` proves it by synthesizing both with one account id and comparing every stack name and physical name:

- **Per-environment names.** Stacks are `<slug>-<env>-<Name>`; buckets `<slug>-<env>-<purpose>-<account>`; secrets and log groups `<slug>/<env>/...`; the cluster, service, task family, load balancer, ECR repository, alarm topic and KMS alias, Prometheus alias, RDS instance and its log groups, MediaConvert queue, template, role, topic, and rule, Location place index and map, the metrics namespace, CloudFront functions, policies, OAC, key group and public key, SES configuration set, the deploy role, the alarms, and the budget all carry `<slug>-<env>`. Domains differ (`dev.` prefix), so CloudFront alternate names, certificates, and SES identities differ too. The dev and prod VPCs use different CIDR ranges.
- **The GitHub OIDC provider** exists once per account and issuer, so it is not in either environment's `GitHubDeploy` stack: it lives in the account-level stack `TrellisKit-GitHubOidc` (`GitHubSettings.OidcProviderStackName`), which dev and prod (and every property in the account) synthesize with an identical template. Whichever environment deploys first creates it, the other's deploy is a no-op, and the provider is retained if the stack is deleted. Each deploy role trusts the provider by its fixed ARN (`arn:aws:iam::<account>:oidc-provider/token.actions.githubusercontent.com`) and only its own GitHub environment (`environment:dev` or `environment:prod`). With a provider created outside these stacks, set `GitHubSettings.ExistingOidcProviderArn` and the account stack is not created.
- **Budgets.** Every stack carries the cost allocation tag `Deployment=<slug>-<env>` (`<deploymentId>-<env>` for every stack of a shared deployment). When the other environment resolves to the same account (`EnvironmentConfig.SharedAccount`, from `account:<env>` context values, the deployment file, or `deploy/infra.json`), each budget is filtered to its own tag value, so dev's 120 USD budget does not fire on prod's spend. Activate the `Deployment` tag once under Billing, Cost allocation tags (manual step 16); untagged costs (some data transfer, support, tax) then count in neither budget.
- **The shared-topology site log grant** of the access log bucket is limited to `*-<env>-website-<account>` buckets, so one environment's website buckets cannot log into the other's bucket.
- **Shared per account and region** (not a collision, but worth knowing): the SES sending quota and sandbox status, service quotas (VPCs, Elastic IPs: dev uses 1, prod 2, default quota 5), the CDK bootstrap stacks, and the MediaConvert endpoint. The CloudFront read grant of the buckets covers the account's distributions (see Known limitations).


## Deployment topology

One synthesis deploys either a single property (`-c propertyDir=<folder>`, the default and unchanged behavior) or every property listed in a deployment file (`-c deployment=<file>`):

```json
{
  "id": "platform",
  "name": "Example Platform",
  "topology": "Shared",
  "properties": ["../samples/example", "../../other-property"],
  "platform": { "hosts": ["platform.example.test"], "bootstrapAdminEmail": "admin@example.test" },
  "accounts": { "dev": "111111111111", "prod": "222222222222" },
  "alarmEmail": "alerts@example.test",
  "github": { "owner": "<github owner>", "repository": "<repository>" },
  "mail": { "mailgunDomain": "mg.example.test" }
}
```

Property paths are relative to the folder that contains the deployment file (or absolute). The first property is the primary one. `accounts`, `alarmEmail`, and `github` are optional. `platform.hosts` is required for `Shared` (the backend refuses to start in MultiProperty mode without a platform host): each is a production host name under one listed property's web domain, and dev uses the same label under that property's dev domain (`platform.example.test` becomes `platform.dev.example.test`). `platform.bootstrapAdminEmail` is optional. Property ids and slugs must be unique within the file, and the deployment id must differ from every slug.

| `topology` | Stacks | Backend mode |
|---|---|---|
| `PerProperty` (default) | One full stack set per listed property, exactly as with `propertyDir`: `<slug>-<env>-<Name>` for all 13 to 15 stacks (Redirect only with vanity domains, Email only with the SES identity), plus the account-level `TrellisKit-GitHubOidc` stack once per account. Each property may use its own account (`accounts` in the deployment file wins, else the property's `deploy/infra.json`) | `SingleProperty` |
| `Shared` | One shared set named `<deploymentId>-<env>-<Name>`: Network, Storage, Observability, Registry, Data, MediaPipeline, Api, Alarms, Budget, GitHubDeploy (plus the account-level `TrellisKit-GitHubOidc`). Plus per property `<slug>-<env>-<Name>`: Dns, EdgeCertificates, Media, Web, Redirect (only with vanity domains), Email (only with the SES identity, decided by the deployment file's `mail.provider`). All in one account | `MultiProperty` (`TK_DEPLOYMENT_MODE`) |

How the shared topology fits together:

- **API.** One ALB and ECS service. The HTTPS listener carries every property's regional API certificate (SNI; the first property's is the default) and the Api stack writes the `api.<domain>` alias record into each property's zone. The backend runs `MultiProperty` and resolves each request from `X-TK-Property` and `X-TK-App` (apps) or the host (websites: CloudFront sets `X-Forwarded-Host` to the property's web domain; direct API calls carry `api.<domain>` in `Host`), `docs/TRELLISKIT.md` section 10.1. Each platform host also gets a regional certificate on the listener and an alias record in its property's zone, and the server receives them as `TK_PLATFORM_HOSTS`. An ALB accepts 25 certificates per listener besides the default, so a shared deployment lists at most 25 properties and at most 26 property API hosts and platform hosts together.
- **Data and storage.** One database and one set of media and updates buckets; the backend prefixes object keys with the property id. Upload CORS and `TK_CORS_ORIGINS` allow every property's website origins.
- **Per property.** Each property keeps its own hosted zones, CloudFront certificates (us-east-1), media distribution (`media.<domain>`, serving the shared buckets and trusting the shared signing key group), website distribution, SES identity, and redirect. Its website bucket lives in its own Web stack (`<slug>-<env>-website-<account>`) and only that property's distribution can read it; no certificate covers another property's hosts.
- **Per-property settings.** The shared API does not receive `TK_WEB_PUBLIC_BASE_URL`, `TK_SMTP_FROM_ADDRESS`, `TK_MEDIA_PUBLIC_BASE_URL`, `TK_MEDIA_COOKIE_DOMAIN`, `TK_UPDATES_PUBLIC_BASE_URL`, or the store identifiers; the backend reads them from each property's configuration in MultiProperty mode (section 10.4). The server image still bakes one property into `/app/property` (build it with the primary property).
- **Secrets and webhooks.** One server secret, media signing key, updates signing key, application database secret, and platform secret (`TK_PLATFORM_BOOTSTRAP_ADMIN_PASSWORD`, the only source of that value) under `<deploymentId>/<env>/...`. Per-property integration secrets (Stripe, stores, sign-in) are entered in each property's admin portal and stored encrypted in the database; the infrastructure injects no `TK_PROPERTY_<ID>_*` variables (Resolved decision 3 of `docs/MULTIPROPERTY_CONTRACT.md`). MediaConvert job events go to the primary property's API host (every API host reaches the same service). One GitHub deploy role (repository from the deployment file) can push the image, roll the service, and sync and invalidate every property's website.

**Stack names changed with opaque property ids.** Stacks, resource names, and secret paths used to be built from the property id; they now use the slug. Nothing had been deployed when this changed, so no stack was renamed in place. A stack deployed under an old name would be a new stack under the new name (CloudFormation cannot rename), so such an environment would need the same data and DNS move as a topology switch.

Switching an existing property between topologies is a migration, not an update: the shared stacks have different names, so CloudFormation would create new resources. Move data (database dump, `aws s3 sync` of the buckets) and DNS by hand.

### Cost: shared versus per-property

Per property, the per-property topology pays for its own NAT gateways, ALB, Fargate tasks, RDS instance, Prometheus workspace, and Container Insights, roughly 115 USD (dev) and 280 to 320 USD (prod) a month each (see "Cost estimate"). The shared topology pays those once; each additional property adds only its hosted zones (0.50 USD each), certificates (free), three or four CloudFront distributions and an SES identity (usage based, mostly free tier at launch), plus whatever extra Fargate and database capacity its traffic needs. For example three small properties in prod cost about 900 USD a month per-property versus about 320 to 380 USD shared. Trade-offs: a shared deployment shares one blast radius (an outage, a bad release, or a noisy property affects all of them), one database maintenance window, one budget, and one alarm topic; per-property keeps them isolated and lets each property live in its own account.

## Stacks

Deployment order (CDK resolves it with `--all`; the Registry must be deployed and hold an image before the Api stack):

| # | Stack | Region | Contents |
|---|---|---|---|
| 1 | `Network` | us-west-2 | VPC on 2 AZs: public (ALB, NAT), private with egress (Fargate), isolated (RDS); VPC flow logs; security groups for the ALB (public 80 and 443, plus a CloudFront-only group for the origin listener port), tasks, and database |
| 2 | `Dns` | us-west-2 | Route 53 zones for both domains; regional ACM certificate for `api.<domain>`. External DNS: no zones, the certificate is requested by a custom resource and its validation record is an output (also the platform host certificates of a shared deployment) |
| 3 | `EdgeCertificates` | us-east-1 | ACM certificates for the website, media, and vanity distributions (cross-region references). External DNS: requested by a custom resource, validation records as outputs |
| 4 | `Storage` | us-west-2 | One bucket per purpose: website (versioned; per-property topology only); updates (live update bundles, assets, and manifests, versioned); media source and media video source (original uploads, CORS for presigned PUT, never expired, Standard-IA after 30 days and Glacier Instant Retrieval after 180); media images (processed derivatives, versioned); media transcoded (HLS renditions, versioned); media retained (moderator-removed content, expires after 365 days); access logs. All block public access, enforce TLS, SSE-S3 |
| 5 | `Observability` | us-west-2 | Amazon Managed Service for Prometheus workspace; log groups with retention (including `server-metrics`, the embedded metric format records of the metrics alarms read); KMS-encrypted alarm topic with email subscription |
| 6 | `Registry` | us-west-2 | ECR repository (scan on push, keep 30 images) |
| 7 | `Data` | us-west-2 | RDS PostgreSQL 17 (major pinned), gp3 with autoscaling, encrypted, backups and PITR, generated master credentials (`<slug>/<env>/database`) and application role credentials (`<slug>/<env>/database-app`, user `trelliskit_app`) in Secrets Manager, parameter group (`rds.force_ssl`, slow query logging, `pg_stat_statements`), log exports; output `PeakServerConnections` (see "Realtime") |
| 8 | `Media` | us-west-2 | `media.<domain>` CloudFront distribution: images bucket by default, transcoded bucket under `/*/video/*`; signed cookies required except `/*/public/*` and `/*/public/video/*` (public sample posts). Every media object key starts with the property id (`<propertyId>/video/<id>/...`, `<propertyId>/public/...`, see the backend's `MediaKeys` helper), so the patterns begin with a wildcard segment and the backend scopes each signed cookie policy to `https://media.<domain>/<propertyId>/*`. Documents (`/*/media/*.pdf`, and `/*/public/media/*.pdf` without cookies, ahead of the video and public patterns) get the `media-documents` response headers policy: `Content-Disposition: attachment`, `Content-Security-Policy: sandbox`, and `X-Content-Type-Options: nosniff`, the headers the backend sends when it serves a PDF itself (`docs/PHASE4_CONTRACT.md` section 5.3). Updates bucket under `/updates/*` without cookies (its behaviors come first, so a media pattern can never capture an updates path): `/updates/assets/*` cached immutably for a year (`Cache-Control: public, max-age=31536000, immutable`), every other `/updates/*` object (manifests) never cached (`no-cache, no-store, must-revalidate`); one shared OAC; key group, signing private key secret, MediaConvert role, on-demand MediaConvert queue and job template (`<slug>-<env>-hls`), EventBridge rule to an encrypted SNS topic with an HTTPS subscription to `/api/v1/webhooks/mediaconvert` |
| 9 | `Api` | us-west-2 | ECS cluster (Container Insights), Fargate service with ADOT sidecar and the `db-bootstrap` init container (pgvector, the application role, the search schema, and RecallDB's database and role when enabled), the optional RecallDB service (`ai.recallDb`: Fargate X86_64 in the private subnets, private DNS namespace `<prefix>.internal`, its three secrets), ALB (public HTTPS on 443 that refuses `CloudFront-Viewer-Address`, CloudFront-only HTTPS on 8443, both with a `/api/v1/ws` rule to the target group, HTTP 301 to HTTPS, 120 s idle timeout, `/api/v1/health/ready` health check, 30 s deregistration delay, no stickiness), server stop timeout 30 s, CPU and request autoscaling, `api.<domain>` alias, application secret, updates signing key secret, push secret, engine secrets key (`<slug>/<env>/secrets-key`, filled once by a custom resource), Mailgun secret when `mail` uses Mailgun, Partio secret with `partio`, place index and map with `location`; shared topology: platform host certificates and aliases, platform secret |
| 10 | `Web` | us-west-2 | Website distribution for apex and www: S3 origin via OAC with routing function, `/api/*`, `/openapi.json`, `/swagger*` to the API's CloudFront origin listener uncached (with `CloudFront-Viewer-Address`), dedicated uncached behaviors for `/api/v1/updates/manifest` and `/api/v1/app/manifest` ahead of `/api/*`, `/embed/assistants/*` with its own `frame-ancestors` headers policy, security headers policy |
| 11 | `Redirect` | us-west-2 | Only when the property has `domains.redirects`: one distribution for every vanity apex and www host, CloudFront Function 301 to the website preserving path and query |
| 12 | `Email` | us-west-2 | Only when `mail.provider` is not `Mailgun` or `None`: SES domain identity with DKIM, MAIL FROM `mail.<domain>` (MX and SPF), DMARC record, configuration set |
| 13 | `Alarms` | us-west-2 | 5xx rate, unhealthy targets, no healthy targets, RDS CPU, RDS free storage, realtime bus notification queue usage above 0.5 |
| 14 | `Budget` | us-east-1 | Monthly cost budget with actual and forecast email notifications |
| 15 | `GitHubDeploy` | us-west-2 | Deploy role (trusts the account's GitHub OIDC provider) |
| | `TrellisKit-GitHubOidc` | us-west-2 | Account-level: the GitHub OIDC provider, shared by every environment and property in the account (not named per environment; see "One account for dev and prod"). Deployed before `GitHubDeploy` |

Stack names are `<slug>-<env>-<Name>`, for example `example-prod-Api` or `example-dev-Network`. In the shared topology the shared stacks are `<deploymentId>-<env>-<Name>` and the media pipeline (signing keys, MediaConvert role, job events) is its own `MediaPipeline` stack, while each property's `Media` stack holds only its distribution.

### Notable decisions

- **Same-origin API.** The website distribution forwards the API path patterns with caching disabled (`CachingDisabled`, so no API response is cached or shared across viewers or properties) and the `AllViewerExceptHostHeader` origin request policy: every header (including `X-TK-Property` and `X-TK-App`), cookie, and query string except `Host`. Forwarding `Host` would make CloudFront validate the ALB certificate against the website apex and fail. A viewer-request function on those behaviors therefore copies the viewer's `Host` into `X-Forwarded-Host` (replacing any client value), and the server resolves the property from it (`TK_TRUST_FORWARDED_HOST=true`). CloudFront appends the viewer address to `X-Forwarded-For`. The managed `AllViewerExceptHostHeader` policy also adds CloudFront's viewer location headers, `CloudFront-Viewer-Address` (viewer `ip:port`) among them, so the policy is kept rather than replaced: a custom policy cannot say "every viewer header except Host plus a CloudFront header" (`allExcept` takes no CloudFront headers and `allViewerAndWhitelistCloudFront` forwards Host). `CdnTests` and `EdgeSecretsAndMailTests` pin the policy id on every API behavior.
- **Load balancer headers.** The ALB passes `Host`, `X-Forwarded-Host`, `X-TK-Property`, and `X-TK-App` through unchanged (`routing.http.preserve_host_header.enabled`; header names are valid, so `drop_invalid_header_fields` keeps them) and appends its peer to `X-Forwarded-For` (`xff_header_processing.mode=append`). The server trusts `X-Forwarded-For` only from the VPC: `TK_TRUSTED_PROXIES` is the VPC CIDR, so the client address is the right-most `X-Forwarded-For` entry outside it.
- **Client addresses behind CloudFront.** The server's socket peer is always the ALB, which it trusts, so it believes a `CloudFront-Viewer-Address` header (backend `rest.viewerAddressHeader`, default `CloudFront-Viewer-Address`) on any request that reaches it. Only CloudFront may therefore send one:
  - The website distributions use a second HTTPS listener on `ApiSettings.CloudFrontOriginHttpsPort` (default 8443, the API host certificates by SNI) as their API origin. Its own security group admits that port only from the AWS managed prefix list `com.amazonaws.global.cloudfront.origin-facing` (`NetworkSettings.CloudFrontOriginFacingPrefixListId`, context `cloudFrontPrefixListId`; ids differ per region, print one with `aws ec2 describe-managed-prefix-lists --region <region> --filters Name=prefix-list-name,Values=com.amazonaws.global.cloudfront.origin-facing`). The list weighs about 55 rules against the 60 rules-per-group quota, which is why it has a group of its own.
  - The public listener on 443 stays open to the internet for the apps, webhooks, and platform hosts (restricting it would break mobile apps, which call the ALB directly), and a listener rule refuses any request on it that carries `CloudFront-Viewer-Address` (`400`, `ApiSettings.ViewerAddressHeader`). Apps and webhook senders never send it.
  - `TK_ADDITIONAL_TRUSTED_PROXIES` (live) adds ranges the server trusts in `X-Forwarded-For`, which matters only when the viewer header is missing or disabled. The backend needs literal ranges, not a prefix list id, so it is unset by default and set only when configured: pass the IPv4 `CLOUDFRONT_ORIGIN_FACING` ranges (the ALB is IPv4 only) with `-c additionalTrustedProxies="$(curl -s https://ip-ranges.amazonaws.com/ip-ranges.json | jq -r '[.prefixes[] | select(.service=="CLOUDFRONT_ORIGIN_FACING") | .ip_prefix] | join(",")')"` on every deploy (for example in the deploy workflow), so a refresh is a redeploy. Entries are validated at synth.
- **Database bootstrap and least privilege.** The server connects as `trelliskit_app` (`TK_DB_USERNAME` and `TK_DB_PASSWORD` from `<slug>/<env>/database-app`), a `LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE` role with `CONNECT`, `TEMPORARY`, and `CREATE` on the database (`CREATE` only for the search schema check, see "Live updates, search, and AI"), `USAGE` and `CREATE` on schema `public` (its migrations create and own the tables; `PUBLIC` loses `CONNECT` and `CREATE`), and ownership of the search schema, as `docs/TRELLISKIT.md` section 13 requires and the Docker init script does locally. The master credentials reach only the `db-bootstrap` container of the API task: a non-essential `postgres:17-alpine` container that waits for the instance (`pg_isready`, up to `DatabaseSettings.BootstrapWaitSeconds`), then as the master user runs idempotent SQL (`ApiStack.BootstrapSql`): create the role if missing, set its password from the secret, apply the grants, create the search schema, `CREATE EXTENSION IF NOT EXISTS vector`, and, when RecallDB is enabled, `ApiStack.RecallDbBootstrapSql` for RecallDB's role and database. The server container depends on it with condition `SUCCESS`, so a task never starts the server before the role and pgvector exist, and a failure shows in the `db-bootstrap` log stream of the server log group. This was chosen over a custom resource Lambda because it needs no bundled PostgreSQL driver, runs inside the VPC with the task's existing network path, re-applies the role password on every start (secret rotation is "change the secret, redeploy"), and self-heals if the role or extension is dropped.
- **SPA routing without error pages.** A viewer-request function serves `/index.html` for extensionless paths (`/app/*`, `/admin/*`, and marketing routes) and redirects www to the apex. Distribution-wide custom error responses are not used because they would also replace API 403 and 404 bodies. When the website build starts emitting prerendered pages (`/about/index.html` and so on), list them in `WebSettings.PrerenderedRoutes`.
- **OAC bucket grants.** The storage stack grants `s3:GetObject` to CloudFront distributions of the account (`AWS:SourceAccount` plus `AWS:SourceArn` of `distribution/*`). A grant per distribution would make the storage and CDN stacks depend on each other.
- **Secrets.** Every secret reaches the container through ECS `secrets` from Secrets Manager: `TK_DB_USERNAME` and `TK_DB_PASSWORD` from the application role secret (never the RDS master secret), `TK_MEDIA_SIGNING_PRIVATE_KEY` from the media secret, `TK_UPDATES_SIGNING_KEY` from `<slug>/<env>/updates-signing-key`, `TK_UPDATES_STANDBY_SIGNING_KEY` from `<slug>/<env>/updates-standby-signing-key`, `TK_RECALLDB_TOKEN` from `<slug>/<env>/recalldb-token` (only with RecallDB), `TK_EXPO_ACCESS_TOKEN` from `<slug>/<env>/push` (a separate secret, so adding it never regenerates the server secret's template and wipes filled values), `TK_SECRETS_ENCRYPTION_KEY` and `TK_SECRETS_PREVIOUS_ENCRYPTION_KEYS` from `<slug>/<env>/secrets-key`, `TK_MAILGUN_API_KEY` from `<slug>/<env>/mailgun` (only when the `mail` section uses Mailgun), `TK_PARTIO_API_KEY` from `<slug>/<env>/partio` (only with `partio.baseUrl`), every key of `<slug>/<env>/server` (see `Stacks/ServerSecretKeys.cs`), and in the shared topology `TK_PLATFORM_BOOTSTRAP_ADMIN_PASSWORD` from `<deploymentId>/<env>/platform`. Plain environment variables carry only hostnames, ports, URLs, bucket names, ARNs, any pinned live settings, `TK_PROPERTY_DIR=/app/property`, and the property's public store identifiers.
- **Engine secrets key.** `TK_SECRETS_ENCRYPTION_KEY` (base64 of 32 bytes; encrypts per-property integration secrets such as Mailgun keys and queued email payloads) must be the same on every task, so ECS always injects it. Secrets Manager's generator cannot produce base64 of exactly 32 bytes, so `Stacks/SecretsKeySecret.cs` creates `<slug>/<env>/secrets-key` with empty keys and a custom resource (an inline Node.js function, `Custom::SecretsKeyInitializer`) that writes `crypto.randomBytes(32)` as base64 into it when the key is empty. It never replaces a key that is set, never logs or returns it, and does nothing on delete; the ECS service depends on it, so no task starts without a key. The secret's template value is a constant pinned by a test and must never change (CloudFormation would write it over the stored key). It is retained in prod; in dev it is deleted with the stack, so a restored dev database snapshot needs the old key put back.
- **Property in the image.** The server image bakes the property folder into `/app/property` (named build context `property`, see `docker/server/Dockerfile`), so tasks read `property.json` and brand assets without a volume.
- **Telemetry.** The server exports OTLP gRPC to the ADOT sidecar on `127.0.0.1:4317` (`TK_OTLP_ENDPOINT`). The collector sends metrics to Managed Prometheus (SigV4), traces to X-Ray, and OTLP logs to the `server-otlp-logs` log group. Container stdout goes to the `server` log group.
- **Registry stack.** ECR is separate from the Api stack so an image can be pushed before the ECS service is first created.
- **Alarms stack.** Alarms are separate from Observability: the Api stack needs the Prometheus workspace and log groups (so Observability deploys first), while alarms need the ALB and database (so they deploy last).
- **Live updates on the media host.** `docs/TRELLISKIT.md` section 8.3 places the updates path behind the API domain, but the API host is an ALB, not CloudFront, so the path lives on the media distribution (`https://media.<domain>/updates/...`, `TK_UPDATES_PUBLIC_BASE_URL`). Object keys mirror URL paths: a hashed asset at `/updates/assets/<sha256>` is the key `updates/assets/<sha256>`. Only content-addressed objects belong under `updates/assets/`; anything mutable goes elsewhere under `updates/` and is never cached. The dynamic manifest endpoint (`GET /api/v1/updates/manifest`) stays on the API and is never cached (the website's `/api/*` behavior disables caching; the ALB has no cache). The task role can read, write, and delete objects in the updates bucket.
- **Outbound HTTPS.** The task security group allows all outbound traffic through the NAT gateways (there are no network ACLs or firewalls besides), which covers Mailgun (`api.mailgun.net`, `api.eu.mailgun.net`, deployment mail and each property's own Mailgun account), the DNS-over-HTTPS resolver of domain verification (`TK_DOMAIN_VERIFICATION_RESOLVER_URL`, default `https://cloudflare-dns.com/dns-query`), AI model providers (section 9; their credentials are stored encrypted in the database, not in Secrets Manager), stores, payment, and sign-in providers. `EdgeSecretsAndMailTests.TasksHaveOutboundHttps` guards it.
- **Media buckets per purpose.** Originals are never deleted by lifecycle (every original upload is kept, moving to colder storage as it ages); derivatives can always be regenerated from them. The server task role can read, write, and delete objects in all five media buckets (presigned uploads, derivative writes, moves into the retained bucket, and erasure of a deleted member's media); MediaConvert reads only the video source bucket and writes only the transcoded and images buckets. The task role's MediaConvert access is `mediaconvert:CreateJob` on this account's jobs in the region plus the stack's queue and job template, `mediaconvert:GetJob` on the jobs (the polling fallback), `mediaconvert:DescribeEndpoints` (no resource-level support), and `iam:PassRole` on the MediaConvert role only, conditioned on `iam:PassedToService=mediaconvert.amazonaws.com`. It has no SNS permissions: the webhook confirms the subscription by fetching the signed `SubscribeURL` and verifies every message signature, signing certificate host, and topic ARN server-side. The server receives each bucket as `TK_MEDIA_SOURCE_BUCKET`, `TK_MEDIA_VIDEO_SOURCE_BUCKET`, `TK_MEDIA_IMAGES_BUCKET`, `TK_MEDIA_TRANSCODED_BUCKET`, and `TK_MEDIA_RETAINED_BUCKET`; the admin console can override each (storage settings, see `docs/PHASE2_CONTRACT.md`).
- **Server variable names.** Every storage, media, push, feature, updates, and deployment variable name is defined once in `Stacks/ServerEnvironmentKeys.cs`. Media names match the backend settings registry: `TK_MEDIA_STORAGE_MODE=S3`, `TK_MEDIA_S3_REGION`, the five `TK_MEDIA_*_BUCKET` names, `TK_MEDIA_PUBLIC_BASE_URL`, `TK_MEDIA_COOKIE_DOMAIN` (the web domain, so cookies cover the media host), `TK_MEDIA_VIDEO_PROCESSOR=MediaConvert`, `TK_MEDIA_SIGNING_KEY_PAIR_ID`, `TK_MEDIA_SIGNING_PRIVATE_KEY`, `TK_MEDIACONVERT_ROLE_ARN`, `TK_MEDIACONVERT_QUEUE_ARN` (the stack's queue), `TK_MEDIACONVERT_JOB_TEMPLATE` (the stack's template name), `TK_MEDIA_SNS_TOPIC_ARN`. `TK_FFMPEG_PATH` is not set (production never uses ffmpeg). `TK_PUSH_ENABLED`, `TK_LINK_PREVIEWS_ENABLED`, `TK_EMAIL_FEED_POSTS_ENABLED`, and `TK_ACCOUNT_EXPORT_RETENTION_HOURS` are live settings admins edit in the console, and an environment variable overrides and locks a setting, so they are not set by default (the backend defaults and the console apply). Set `ApiSettings.PushEnabled`, `LinkPreviewsEnabled`, `EmailFeedPostsEnabled`, or `AccountExportRetentionHours` (1 to 168) only to pin a value; each configured one is passed to the server and locks that setting (`ServerEnvironmentKeys.OptionalLiveKeys`). `TK_DEPLOYMENT_MODE` is `SingleProperty` (per-property topology) or `MultiProperty` (shared). Restart settings are always set: `TK_DEPLOYMENT_NAME` (the slug, or the deployment id when shared), `TK_TRUST_FORWARDED_HOST` (`ApiSettings.TrustForwardedHost`, default true). `TK_TRUSTED_PROXIES` (the VPC CIDR) is a live setting but is set always, because the network decides it; it locks that console field. The live platform settings follow the live rule: `TK_PLATFORM_HOSTS` and `TK_PLATFORM_BOOTSTRAP_ADMIN_EMAIL` are set only when the deployment file's `platform` section configures them (shared topology only). So do `TK_ADDITIONAL_TRUSTED_PROXIES` (context `additionalTrustedProxies`), `TK_DOMAIN_VERIFICATION_RESOLVER_URL` (`ApiSettings.DomainVerificationResolverUrl`), and `TK_EMAIL_PROVIDER`, `TK_MAILGUN_DOMAIN`, `TK_MAILGUN_BASE_URL` (the `mail` section), and `TK_PARTIO_BASE_URL` (the `partio` section). `TK_SMTP_HOST`, `TK_SMTP_PORT`, and `TK_SMTP_USE_TLS` (the SES SMTP endpoint) are set only while the SES identity exists (`mail.provider` other than `Mailgun` and `None`). The directory geocoder and map settings (`directory.geocoder.*`, `directory.map.*`) are per-property console settings with no environment variables, so the `location` section never sets them; `TK_VIEWER_ADDRESS_HEADER` is never set (the backend default is the CloudFront header). `TK_SECRETS_ENCRYPTION_KEY` and `TK_SECRETS_PREVIOUS_ENCRYPTION_KEYS` are restart settings and always injected. There is no `TK_DB_FILENAME` (PostgreSQL only) and no `TK_PROPERTY_ID` variable: the server takes the property, and its opaque id, from `property.json` in `TK_PROPERTY_DIR`. The updates names match the backend registry: `TK_UPDATES_STORAGE_MODE=S3`, `TK_UPDATES_S3_REGION`, `TK_UPDATES_BUCKET`, `TK_UPDATES_PUBLIC_BASE_URL` (live, but set always because the CDN decides it), and the secrets `TK_UPDATES_SIGNING_KEY` and `TK_UPDATES_STANDBY_SIGNING_KEY`; `TK_UPDATES_ENABLED` and the key ids are console settings and never set. `TK_RECALLDB_ENDPOINT` and `TK_RECALLDB_TENANT` are set only when RecallDB is enabled, and `TK_AI_ENABLED`, `TK_SEARCH_ENABLED`, and `TK_SEARCH_SCHEMA` are never set (backend defaults and the console apply).

- **MediaConvert job template coupling.** The backend (`MediaConvertTranscoder`) creates each job from the template and overrides only the destinations of output group 0 and output group 1, matched by position. `Stacks/MediaConvertJobTemplate.cs` therefore keeps output group 0 as the HLS group (an H.264 QVBR ladder from `MediaSettings.HlsRenditions`, default 1080p, 720p, 360p, AAC stereo, `SINGLE_DIRECTORY`, so the backend's `<propertyId>/video/<id>/master` destination yields `master.m3u8`, `master_720p.m3u8`, `master_720p_00001.ts` in one flat folder) and output group 1 as the frame capture file group (`poster_frame.0000000.jpg` 1280 wide and `poster_thumb.0000000.jpg` 320 wide under `<propertyId>/media/<id>/poster`, which the backend renames to `poster.jpg` and `poster_thumb.jpg`). Input 0 defines `Audio Selector 1` for the renditions; the job's input is merged with it. Do not reorder, insert, or remove output groups, and give the template no destinations of its own. A test (`MediaWiringTests.JobTemplateMatchesTheBackendLayout`) guards this layout.
- **Download rendition (opt-in, off).** Offline lesson downloads (`docs/PHASE4_CONTRACT.md` section 5.9) look for `<propertyId>/video/<id>/download_720p.mp4` in the transcoded bucket (backend `MediaKeys.DownloadFileName`; `MediaPipeline` marks the rendition present when that object exists). `MediaSettings.DownloadRendition` appends output group 2 (`Download`: one MP4 output, name modifier `_720p`, H.264 QVBR at 720 lines with `MediaSettings.DownloadMaxBitrate`, AAC stereo, `PROGRESSIVE_DOWNLOAD` moov placement) after the HLS and poster groups, which never move. It is off because the backend's `MediaConvertTranscoder` overrides only the destinations of groups 0 and 1 and the template has no destinations, so a third group would fail every job. Turn it on only once the backend also sets group 2's destination to `s3://<transcoded>/<propertyId>/video/<id>/download` (written as `download_720p.mp4`). Until then `download_720p` never appears on AWS and downloads stay unavailable there (the ffmpeg pipeline is unaffected).
- **Account exports.** The backend writes account data exports to `{storage.directory}/exports/` on the task's local disk, not to S3, so there is no export bucket. See Known limitations.

### cdk-nag suppressions

AwsSolutions checks run on every synth; unsuppressed findings fail synthesis and the tests. Each suppression is recorded with its reason in the template metadata.

| Rule | Where | Reason |
|---|---|---|
| EC23 | ALB security group | Public HTTPS entry point for the apps and website; port 80 only redirects |
| S1 | Access log bucket | It is the log destination; logging it to itself would recurse |
| CFR1 | All distributions | Service is offered worldwide |
| CFR2 | All distributions | WAF deferred for cost at launch; add a us-east-1 web ACL when traffic warrants |
| ECS2 | Task definition | Only non-secret configuration is in environment variables |
| IAM5 | Task role and execution role | Object wildcards on the five media buckets and the updates bucket, the streams of the OTLP logs and metrics log groups, MediaConvert jobs of the account and region (job ids are assigned by the service), and `Resource::*` for `mediaconvert:DescribeEndpoints`, X-Ray, and `ecr:GetAuthorizationToken` (no resource-level support). Scoped with `appliesTo` patterns |
| IAM5 | MediaConvert role | Object wildcards on the video source, transcoded, and images buckets |
| IAM5 | GitHub deploy role | `ecr:GetAuthorizationToken` requires `*`; object wildcards on the website buckets (one per property in the shared topology) for `s3 sync --delete` |
| IAM5 | Certificate requester function role (external DNS only) | `acm:RequestCertificate` has no resource-level permissions; describe, tag, and delete use `certificate/*` of the account and region because ACM assigns the ARN at request time |
| RDS11 | Database | Default port; isolated subnets and a single allowed security group |
| SMG4 | Database master and application role, server, media signing, push, platform, Mailgun, Partio secrets | Automatic rotation would change values under running tasks or at third parties; rotated manually |
| SMG4 | Engine secrets key | Rotation needs the previous key listed until every stored value is rewritten; rotated manually |
| SMG4 | Updates signing key secret | Its certificate is embedded in shipped app binaries, so rotation needs a store release; rotated manually |
| RDS3 | Database (dev only) | Single-AZ in dev for cost |
| RDS10 | Database (dev only) | Dev can be torn down; both snapshot on delete |

## Prerequisites

- .NET SDK 10.0
- Node.js 22 or later (the CDK CLI runs through `npx aws-cdk@2.1147.0`; no global install needed)
- AWS CLI v2 and credentials for the target account (deploy only)
- Docker (to build and push the server image)

## Build, test, synth, diff

```bash
cd infra
dotnet build src/TrellisKit.Infra.sln           # 0 warnings (warnings are errors)
dotnet test src/TrellisKit.Infra.sln

# The sample property (engine default)
npx aws-cdk@2.1147.0 synth -c env=dev  --no-lookups --quiet --output cdk.out/dev
npx aws-cdk@2.1147.0 synth -c env=prod --no-lookups --quiet --output cdk.out/prod

# A property (absolute path, or relative to the engine root)
npx aws-cdk@2.1147.0 synth -c env=prod -c propertyDir=<property folder> --no-lookups --quiet --output cdk.out/<slug>-prod

# A shared deployment (the test fixture: the sample plus a second fixture property)
npx aws-cdk@2.1147.0 synth -c env=prod -c deployment=infra/fixtures/shared/deployment.json --no-lookups --quiet --output cdk.out/shared-prod

# Against a real account (needs credentials):
npx aws-cdk@2.1147.0 diff -c env=prod -c propertyDir=<folder> --all
```

Put the real account ids in the property's `deploy/infra.json` (or always pass `-c account=`) before any command that talks to AWS.

## Bootstrap

Bootstrap both regions once per account. us-east-1 is needed for the CloudFront certificates, the budget, and the cross-region reference parameters.

```bash
cd infra
npx aws-cdk@2.1147.0 bootstrap -c env=prod -c propertyDir=<folder> -c account=<ACCOUNT_ID> \
  aws://<ACCOUNT_ID>/us-west-2 aws://<ACCOUNT_ID>/us-east-1
```

Repeat with `-c env=dev` and the dev account id. When dev and prod share one account, bootstrap once (the bootstrap stacks are per account and region).

## Deploy

```bash
cd infra
CTX="-c env=prod -c propertyDir=<folder>"     # accounts and alarm email from <folder>/deploy/infra.json
SLUG=<property slug>                          # slug from <folder>/property.json (stack and resource names use it)

# Route 53 DNS mode (the default). With dns.mode External follow "Managing DNS yourself" instead.

# 1. Zones first. The Dns stack waits for the API certificate to validate, which only happens once
#    the registrar points at the new zones (step 2). Run the next step while this one is in progress.
npx aws-cdk@2.1147.0 deploy $CTX $SLUG-prod-Dns

# 2. Registrar: set the nameservers of the primary domain and every vanity domain (Route 53 console, or the stack outputs
#    PrimaryZoneNameServers and RedirectZoneNameServers). See "Manual steps".

# 3. Image repository, then push the server image with the property baked in (from the engine root).
npx aws-cdk@2.1147.0 deploy $CTX $SLUG-prod-Registry
REPO=<ACCOUNT_ID>.dkr.ecr.us-west-2.amazonaws.com/$SLUG-prod-server
aws ecr get-login-password --region us-west-2 | docker login --username AWS --password-stdin ${REPO%%/*}
docker build -f docker/server/Dockerfile --build-context property=<folder> -t $REPO:latest -t $REPO:$(git rev-parse --short HEAD) .
docker push --all-tags $REPO

# 4. Everything else, in dependency order. Fill the server secret (Manual steps) before the Api
#    stack's tasks start, or redeploy the service afterwards.
npx aws-cdk@2.1147.0 deploy $CTX --all
```

Order that `--all` follows: Network, Dns, EdgeCertificates, Storage, Observability, Registry, Data, Media, Api, Web, Redirect (when the property has vanity domains), Email (unless `mail.provider` is `Mailgun` or `None`), Alarms, Budget, TrellisKit-GitHubOidc, GitHubDeploy.

## Managing DNS yourself

With `"dns": { "mode": "External" }` in `deploy/infra.json` (or the deployment file, or `-c dnsMode=External`) the stacks create no hosted zone and no Route 53 record. You create every record at your DNS provider from the stack outputs. Output keys starting with `DnsValidation` are certificate validation records (needed first); keys starting with `DnsRecord` are the service records. Every value reads `name TYPE value`.

Why two steps: an ACM certificate is issued only after its validation CNAME exists, and CloudFront and the load balancer accept only issued certificates. A native CloudFormation certificate would keep its stack in `CREATE_IN_PROGRESS` until then, without the record as an output. So in external mode the `Dns` and `EdgeCertificates` stacks request their certificates through a custom resource (`Custom::DnsValidatedCertificateRequest`) that returns as soon as ACM has published the validation records (seconds), and every stack that uses a certificate (`Api`, `Web`, `Media`, `Redirect`) starts with a check (`Custom::CertificateIssuedCheck`) that waits up to `dns.certificateWaitSeconds` (600) for issuance. If a certificate is still pending, that check fails the deployment with a message listing the missing validation records and the stack whose outputs hold them; nothing else in the stack is created before the check passes.

Steps (per environment; repeat with `env=dev`, whose hosts are the same names under `dev.<domain>`, all records in the same zones):

```bash
cd infra
CTX="-c env=prod -c propertyDir=<folder>"
SLUG=<property slug>

# 1. Certificates only. Both stacks finish in about a minute.
npx aws-cdk@2.1147.0 deploy $CTX $SLUG-prod-Dns $SLUG-prod-EdgeCertificates

# 2. Print the validation records and create each CNAME at your DNS provider (keep them: ACM renews with them).
for r in us-west-2:$SLUG-prod-Dns us-east-1:$SLUG-prod-EdgeCertificates; do
  aws cloudformation describe-stacks --region ${r%%:*} --stack-name ${r#*:} \
    --query "Stacks[0].Outputs[?starts_with(OutputKey,'DnsValidation')].[OutputValue]" --output text
done

# 3. Wait for issuance (usually minutes after the records resolve). The ARNs are the *CertificateArn outputs.
aws acm wait certificate-validated --region us-west-2 --certificate-arn <ApiCertificateArn>
aws acm wait certificate-validated --region us-east-1 --certificate-arn <WebCertificateArn>   # and Media, Redirect

# 4. Registry and image as in "Deploy" step 3, then everything else.
npx aws-cdk@2.1147.0 deploy $CTX --all

# 5. Print the service records and create them.
for s in Api Web Media Redirect Email; do   # Redirect and Email only when they exist
  aws cloudformation describe-stacks --region us-west-2 --stack-name $SLUG-prod-$s \
    --query "Stacks[0].Outputs[?starts_with(OutputKey,'DnsRecord') || OutputKey=='MailDnsRecords'].[OutputKey,OutputValue]" --output text
done
```

The records, by stack (`<domain>` is the environment's primary domain, `dev.<web domain>` in dev):

| Stack | Output | Record |
|---|---|---|
| `Dns` | `DnsValidationApi0` | ACM validation CNAME of `api.<domain>` |
| `EdgeCertificates` (us-east-1) | `DnsValidationWeb0`, `DnsValidationWeb1`, `DnsValidationMedia0`, `DnsValidationRedirect<n>` | ACM validation CNAMEs of `<domain>`, `www.<domain>`, `media.<domain>`, and each vanity apex and www host |
| `Web` | `DnsRecordWebApex`, `DnsRecordWebWww` | `<domain>` and `www.<domain>` to the website distribution (`dxxxx.cloudfront.net`) |
| `Api` | `DnsRecordApi` | `api.<domain>` CNAME to the load balancer (`<slug>-<env>-api-<n>.us-west-2.elb.amazonaws.com`) |
| `Media` | `DnsRecordMedia` | `media.<domain>` CNAME to the media distribution, which also serves live updates (`https://media.<domain>/updates`), so there is no separate updates host |
| `Redirect` | `DnsRecordRedirectApex<n>`, `DnsRecordRedirectWww<n>` | Each vanity apex and its www host to the redirect distribution |
| `Email` | `DnsRecordSesDkim1` to `3`, `DnsRecordSesMailFromMx`, `DnsRecordSesMailFromSpf`, `DnsRecordDmarc` | Only with the SES identity: SES DKIM CNAMEs (the identity verifies once they resolve), MAIL FROM MX `10 feedback-smtp.us-west-2.amazonses.com` and SPF TXT on `mail.<domain>`, DMARC TXT on `_dmarc.<domain>` |
| `Api` | `MailDnsRecords` | Only with `mail.provider` `Mailgun`: not a record but a pointer to the Mailgun dashboard records (below) |
| shared `Api` | `DnsRecordApi<Property>`, `DnsRecordPlatform<Host>` | Shared topology: every property's API host and every platform host CNAME to the shared load balancer; platform host validation records are outputs of the owning property's `Dns` stack (`DnsValidationPlatform<Host>0`) |

Notes:

- **Zone apex.** A record whose name is the web domain or a vanity domain itself reads `ALIAS`: a zone apex cannot hold a CNAME. Use your provider's ALIAS, ANAME, or CNAME flattening record pointing at the distribution's `cloudfront.net` name (CloudFront has no fixed IP addresses, so A records are not an option). If your provider offers none, move the zone to one that does, or serve the site from `www` and redirect the apex at the provider. Dev hosts (`dev.<domain>`) are never apexes and take plain CNAMEs.
- **Values are stable.** Distribution and load balancer names change only if those resources are replaced; redeploys keep them. Validation records stay valid for renewals; do not delete them.
- **Mailgun** records are outside AWS and outside these stacks: when a property or the deployment sends through Mailgun, add the records the Mailgun dashboard (Sending, Domain settings, DNS records) lists for the sending domain (for example `mg.<domain>`) at your provider: the SPF TXT (`v=spf1 include:mailgun.org ~all`) and DKIM TXT (for example `<selector>._domainkey.mg.<domain>`), the two MX records (`mxa.mailgun.org`, `mxb.mailgun.org`, or the EU hosts) if Mailgun should receive bounces and replies, and the tracking CNAME (`email.mg.<domain>` to `mailgun.org`, or the EU host) if click and open tracking is used. With `mail.provider` `Mailgun` no SES record exists, so also add a DMARC TXT on `_dmarc.<domain>` (for example `v=DMARC1; p=quarantine; rua=mailto:dmarc@<domain>; adkim=r; aspf=r`, the policy the Email stack would have created). With the SES identity kept, the Mailgun records do not conflict with the SES records (different names). In Route 53 mode add the same records to the hosted zone by hand (the stacks do not manage them and never remove them).
- **Switching modes** is a DNS move: Route 53 to External removes the zones and records (replicate them at the provider first and lower TTLs); External to Route 53 creates zones whose nameservers you then delegate.


Day-to-day deploys come from GitHub Actions through the deploy role: push the image (built with `--build-context property=<folder>`) tagged with the commit SHA and `latest`, `aws ecs update-service --force-new-deployment`, build the website with `TK_PROPERTY_DIR=<folder>`, `aws s3 sync www/dist s3://<slug>-<env>-website-<account> --delete`, then `aws cloudfront create-invalidation`.

### Live updates, search, and AI

- **Updates CDN.** Hashed assets are stored at `updates/assets/{propertyId}/{sha256}` in the updates bucket and served at `https://media.<domain>/updates/assets/...` with `public, max-age=31536000, immutable`; the rest of `/updates/*` is never cached. `TK_UPDATES_PUBLIC_BASE_URL` is `https://media.<domain>/updates` (the backend appends `/assets/{propertyId}/{sha256}`). Source maps live under `private/sourcemaps/` in the same bucket: no behavior maps to that prefix and the bucket policy lets CloudFront read only `updates/*`, so they are never served.
- **Manifests.** The website distribution has its own behaviors for `/api/v1/updates/manifest` and `/api/v1/app/manifest` (`WebSettings.ManifestPathPatterns`), ordered before `/api/*`: `CachingDisabled` and the managed `AllViewerExceptHostHeader` origin request policy, which forwards every viewer header except Host (so `expo-*`, `X-TK-*` including `X-TK-Install-Id`, `X-Client*`, `Accept`, `If-None-Match`, `Authorization`) and the CloudFront viewer headers (`CloudFront-Viewer-Country`, `CloudFront-Viewer-Address`). A custom policy cannot express the `expo-*`, `X-TK-*`, and `X-Client*` families (allow lists take exact names, ten by default) and cannot drop Host. Mobile apps call `api.<domain>`, which is the ALB directly, so their requests carry no `CloudFront-Viewer-Country` and `country` targeting conditions fail for them (as documented for local mode); upload requests to `/api/v1/admin/updates` have no body limit at the ALB.
- **Signing keys.** `TK_UPDATES_SIGNING_KEY` comes from `<slug>/<env>/updates-signing-key` (unchanged, a generated placeholder until replaced) and `TK_UPDATES_STANDBY_SIGNING_KEY` from the JSON key of the same name in `<slug>/<env>/updates-standby-signing-key`, created empty (the backend treats an empty key as absent). Neither key is ever generated into the repository; see Manual steps.
- **Search schema.** `db-bootstrap` creates the Verbex search schema (`DatabaseSettings.SearchSchema`, default `search`, the backend default of `search.schema`) owned by the application role and grants the role `CREATE` on the database. The server runs `CREATE SCHEMA IF NOT EXISTS search` at start, and PostgreSQL checks the database `CREATE` privilege even when the schema exists, so without it search fails with `permission denied for database`. `CREATE` on a database allows creating schemas only, never databases or roles; the role still does not own the database. The local Docker init script grants the same.
- **AI (RecallDB and the assistant widget).** Off by default: without `ai.recallDb` (or `-c recallDb=true`) no RecallDB task, service, secret, security group, or service discovery namespace exists and nothing is passed to the server, so AI infrastructure costs nothing (model endpoints are entered in the admin console). With it: RecallDB runs the pinned `linux/amd64` image as a Fargate service on X86_64 in the private subnets, without a public IP, registered as `recalldb.<prefix>.internal` in a private DNS namespace of the cluster. Its security group admits only the API tasks on 8600, and the database admits it on 5432. `db-bootstrap` creates its role and database (`ApiStack.RecallDbBootstrapSql`: database owned by the master user, role with `CONNECT` and `TEMPORARY` plus `USAGE` and `CREATE` on its schema `public`, `vector` and `pg_trgm` pre-created) after the application grants. The task runs the same `docker/recalldb/start.sh` as the Docker stack (read at synth time and passed as an environment variable, because the image binds to localhost and ships a well-known admin key with no environment override): it writes RecallDB's settings (all interfaces, TLS to RDS, the admin key from `<slug>/<env>/recalldb-admin-key`), registers the server's credential (`<slug>/<env>/recalldb-token`) on tenant `default`, and deletes the image's seeded credential. The database password is `<slug>/<env>/recalldb-database`. The server receives `TK_RECALLDB_ENDPOINT` and `TK_RECALLDB_TENANT` (live settings, emitted only because the stack decides them when enabled) and `TK_RECALLDB_TOKEN` as an ECS secret. The RecallDB service depends on the API service, so it starts after `db-bootstrap` has created its database. RecallDB keeps its data in PostgreSQL, so tasks are stateless. Cost when on: about 9 USD (0.25 vCPU, 1 GB) plus 0.50 USD for the namespace's hosted zone and 1.20 USD for three secrets per month.
- **Embed frame ancestors.** `/embed/assistants/*` is served from the website bucket through the routing function like any client route, but with its own response headers policy: no `X-Frame-Options`, and `Content-Security-Policy: frame-ancestors 'self' <ai.embedFrameAncestors>` (after `WebSettings.ContentSecurityPolicy` when that is set). Every other path keeps `X-Frame-Options: DENY`. The header comes from CloudFront rather than the server because the embed page is a static file in the website bucket; an assistant's `embed.allowedOrigins` lives in the database, so list every origin any embeddable assistant uses in `ai.embedFrameAncestors` (the embed page also checks the embedding origin against the assistant's own list).
- **TEI** (Hugging Face Text Embeddings Inference) is not provisioned. Run it wherever suits (a GPU instance, a separate ECS service, or a managed endpoint) and add it in the admin console as an endpoint with `apiType` `Tei`. If it runs inside the VPC, allow the API service security group to reach it.

### Realtime (WebSockets)

`docs/PHASE5_CONTRACT.md` section 17. Apps and the website connect directly to `wss://api.<domain>/api/v1/ws` (the ALB, not CloudFront).

- **Load balancer.** ALBs upgrade WebSockets on HTTPS listeners without extra settings. Both HTTPS listeners carry a rule (priority 2, after the viewer address rejection) that forwards `ApiSettings.RealtimePath` (`/api/v1/ws`) to the API target group, the same target as every other path, with no stickiness: the PostgreSQL `LISTEN`/`NOTIFY` bus delivers each event to whichever task holds the socket. The idle timeout is 120 s (`ApiSettings.IdleTimeoutSeconds`, from 3600 s): clients ping every 25 s (`realtime.heartbeatSeconds`) and the server's socket keepalive runs every 30 s, so live sockets never idle out while abandoned HTTP keep-alive connections close sooner.
- **Draining.** Deregistration delay 30 s (`ApiSettings.DeregistrationDelaySeconds`) and server stop timeout 30 s (`ApiSettings.StopTimeoutSeconds`, 30 to 120): on SIGTERM the task stops accepting upgrades, sends `shutdown` with a jittered `reconnectAfterMs`, closes sockets with `1012` over up to 20 s, and deletes its registry rows. `MinHealthyPercent` 100 keeps capacity while sockets move.
- **Database connections.** Each task opens one dedicated `LISTEN tk_realtime` connection besides its pool (`TK_DB_MAX_POOL_SIZE`), so the peak is `MaxTasks * (MaxPoolSize + 1)`, the Data stack output `PeakServerConnections`: dev 2 x 21 = 42, prod 6 x 21 = 126. RDS PostgreSQL's default `max_connections` is `LEAST(DBInstanceClassMemory / 9531392, 5000)`, about 80 to 110 on db.t4g.micro (dev) and about 190 to 220 on db.t4g.small (prod), with 3 reserved for the master user plus the bootstrap container and RecallDB (when on). Dev has ample headroom. Prod uses a pool of 20 per task (decided 2026-11-12) to keep headroom on db.t4g.small; a rolling deployment at 6 tasks briefly runs up to 12 (`MaxHealthyPercent` 200), and pools open connections lazily, so real use is far lower. If prod needs more, lower `ApiSettings.MaxTasks` or move to db.t4g.medium (about 410). Watch `DatabaseConnections` in CloudWatch.
- **Bus alarm.** The server samples `pg_notification_queue_usage()` into the gauge `trelliskit.realtime.bus.queue_usage` (0 to 1). The ADOT collector sends that one metric (a `filter` processor) through the `awsemf` exporter to the `server-metrics` log group, where CloudWatch extracts it into the namespace `TrellisKit/<slug>-<env>` (`ObservabilitySettings.MetricsNamespacePrefix`), and the Alarms stack alarms on a Metrics Insights query, `SELECT MAX("trelliskit.realtime.bus.queue_usage") FROM "TrellisKit/<slug>-<env>"`, above 0.5 (`ObservabilitySettings.RealtimeBusQueueUsageThreshold`) for two periods; missing data (an idle bus or `realtime.bus` `InProcess`) is not breaching. Every other metric still goes only to Managed Prometheus. The task role can write only to that log group (no `cloudwatch:PutMetricData`).
- **Task memory.** An idle socket costs about 20 to 50 KB (buffers and per-connection state), plus its outbound queue under load (`realtime.maxQueuedBytes`). `realtime.maxConnectionsPerTask` (default 10000) can therefore reach 200 to 500 MB per task. Dev tasks (1 GB, 256 MB reserved for the collector) are sized for a few thousand concurrent sockets and prod tasks (2 GB) for about 10000; raise `ApiSettings.TaskMemoryMiB` (with a valid Fargate CPU pairing) or lower `realtime.maxConnectionsPerTask` before expecting more, and watch Container Insights memory. Autoscaling follows CPU and request count, not socket count.

## Manual steps

These cannot be automated by the stacks:

1. **Nameservers** (Route 53 DNS mode; with external DNS create the output records instead, see "Managing DNS yourself"). At the registrar, replace the nameservers of the web domain and each vanity domain with the four Route 53 nameservers of each new zone. For dev, add NS records for `dev.<domain>` of each in the production zones (or at the registrar) pointing at the dev zones' nameservers. ACM validation, and therefore the Dns, EdgeCertificates, and downstream stacks, waits for this.
2. **SES production access** (only with the SES identity, that is `mail.provider` not `Mailgun` or `None`). New accounts are in the SES sandbox (send only to verified addresses). Request production access in the SES console for us-west-2 once the identity verifies.
3. **SES SMTP credentials** (only with the SES identity). Create SMTP credentials in the SES console (it creates an IAM user) and store them as `TK_SMTP_USERNAME` and `TK_SMTP_PASSWORD` in the server secret.
4. **Server secret values** (`<slug>/<env>/server`). `TK_ADMIN_TOKEN` is generated. Fill the rest; empty keys are ignored by the server:
   - `TK_TOKEN_ENCRYPTION_KEY`: `openssl rand -base64 32`. Required before production use: without it each task generates its own key in its ephemeral settings file and sessions break across tasks.
   - Stripe: `TK_STRIPE_SECRET_KEY`, `TK_STRIPE_WEBHOOK_SECRET` (the signing secret of the webhook endpoint in step 10), `TK_STRIPE_PRICE_ID`.
   - App Store: `TK_APPSTORE_APP_APPLE_ID`, `TK_APPSTORE_ISSUER_ID`, `TK_APPSTORE_KEY_ID`, `TK_APPSTORE_PRIVATE_KEY`. (The bundle id and product id come from `property.json`.)
   - Google Play: `TK_GOOGLEPLAY_SERVICE_ACCOUNT_JSON`, `TK_GOOGLEPLAY_PUBSUB_AUDIENCE`, `TK_GOOGLEPLAY_PUBSUB_SERVICE_ACCOUNT`. (The package name and product id come from `property.json`.)
   - Sign-in: `TK_FACEBOOK_APP_ID`, `TK_FACEBOOK_APP_SECRET`, `TK_GOOGLE_WEB_CLIENT_ID`, `TK_GOOGLE_IOS_CLIENT_ID`, `TK_GOOGLE_ANDROID_CLIENT_ID`, `TK_APPLE_SERVICES_ID`. (`TK_APPLE_BUNDLE_ID` comes from `property.json`.)
   - `TK_BOOTSTRAP_ADMIN_EMAIL`.
   Then `aws ecs update-service --cluster <slug>-<env>-cluster --service <slug>-<env>-server --force-new-deployment`.
5. **Updates signing key.** Generate the expo-updates code signing pair (`npx expo-updates codesigning:generate --key-output-directory keys --certificate-output-directory certs --certificate-validity-duration-years 10 --certificate-common-name "<property name>"`), store the private key PEM in `<slug>/<env>/updates-signing-key` (`TK_UPDATES_SIGNING_KEY`), and commit the certificate to the property's `mobile/` folder. The generated value is a placeholder. The standby key (`updates-standby-private-key.pem` from the mobile `updates-certificate` script) goes into the JSON key of `<slug>/<env>/updates-standby-signing-key`: `aws secretsmanager put-secret-value --secret-id <slug>/<env>/updates-standby-signing-key --secret-string "$(jq -Rs '{TK_UPDATES_STANDBY_SIGNING_KEY: .}' updates-standby-private-key.pem)"`. Then force a new ECS deployment and delete the local copies.
6. **Push token.** Optional: create an Expo access token (Expo dashboard, Access tokens) and store it as `TK_EXPO_ACCESS_TOKEN` in `<slug>/<env>/push`, then force a new ECS deployment. Left empty, the server sends unauthenticated Expo pushes (allowed unless the Expo project enforces push security).
7. **Media signing key pair.** Generate the pair outside the repository: `openssl genrsa -out media.pem 2048 && openssl rsa -in media.pem -pubout -out media.pub.pem`. Deploy with `-c mediaSigningPublicKeyPem="$(cat media.pub.pem)"` (or set `MediaSettings.SigningPublicKeyPem`) so the stack's CloudFront public key and key group carry it (`TK_MEDIA_SIGNING_KEY_PAIR_ID` is that public key's id, also the `MediaSigningKeyPairId` output). Then upload the private key out of band: `aws secretsmanager put-secret-value --secret-id <slug>/<env>/media-signing-private-key --secret-string file://media.pem`, force a new ECS deployment, and delete `media.pem` locally. The generated secret value and the default public key are placeholders (the placeholder's private half was discarded), so signed cookies fail until both halves are replaced. Never commit either file. Changing the public key replaces the CloudFront public key (new id), so pass the same PEM on every deploy.
8. **Subscriptions.** Confirm the alarm topic and budget emails. The MediaConvert SNS HTTPS subscription is created before the API exists, so after the API is healthy and handles `SubscriptionConfirmation` on `/api/v1/webhooks/mediaconvert`, use "Request confirmation" on the subscription in the SNS console.
9. **GitHub.** Create GitHub environments `dev` and `prod` (add required reviewers on `prod`) and set the `AWS_DEPLOY_ROLE_ARN` environment secret from each environment's `GitHubDeploy` stack output. The role trusts only `repo:<github.owner>/<github.repository>:environment:<env>` from the property's `deploy/infra.json`, through the provider in the account-level `TrellisKit-GitHubOidc` stack. If the account already has a GitHub OIDC provider created outside these stacks, set `GitHubSettings.ExistingOidcProviderArn` (or import it into that stack) before the first deploy.
10. **Third-party webhooks.** Mailgun (each property's own Mailgun account, configured in its admin portal): `https://api.<domain>/api/v1/webhooks/mailgun/<propertyId>` for delivered, failed, complained, and unsubscribed events; the webhook signing key is that property's Mailgun setting in the admin portal. App Store Server Notifications v2: `https://api.<domain>/api/v1/webhooks/apple`. Google Play RTDN (Pub/Sub push): `https://api.<domain>/api/v1/webhooks/google`. Stripe: one endpoint per property in that property's own Stripe account, `https://api.<domain>/api/v1/webhooks/stripe/<propertyId>`, where `<propertyId>` is the opaque `id` from `property.json` (not the slug); the endpoint's signing secret is that property's `stripe.webhookSecret`. The legacy `/api/v1/webhooks/stripe` (no id) works only in `SingleProperty` mode and returns `404 NotAvailable` in the shared topology, so use the per-property URL everywhere.
11. **Secret rotation.** Rotation is manual (see cdk-nag SMG4). Engine secrets key (`<slug>/<env>/secrets-key`, never rotated by a deploy): generate a key (`openssl rand -base64 32`), put `{"TK_SECRETS_ENCRYPTION_KEY":"<new>","TK_SECRETS_PREVIOUS_ENCRYPTION_KEYS":"<old>[,<older>]"}` (`aws secretsmanager put-secret-value --secret-id <slug>/<env>/secrets-key --secret-string file://key.json`), force a new ECS deployment, and keep each old key in the previous list until every value encrypted with it has been rewritten (re-save integration secrets in the admin portal; queued email drains on its own). Removing a key that still encrypts a stored value makes that value unreadable. Application role: put a new password in `<slug>/<env>/database-app` and force a new ECS deployment; the bootstrap container sets it on the role before the server starts (tasks still running keep their open connections until replaced). Master user: rotate the RDS secret (it is used only by the bootstrap container), then force a new ECS deployment.
12. **Deployment mail.** When the `mail` section uses Mailgun, add the sending domain in the Mailgun dashboard, create its DNS records (see "Managing DNS yourself", Mailgun, which applies in both DNS modes, plus DMARC when there is no SES identity), put the Mailgun API key of the sending domain into `TK_MAILGUN_API_KEY` of `<slug>/<env>/mailgun` (`<deploymentId>/<env>/mailgun` when shared), set the sending domain and region in the admin console unless `mail.mailgunDomain` and `mail.mailgunBaseUrl` pin them, and force a new ECS deployment. Left empty, the backend treats the key as unset and falls back as `TK_EMAIL_PROVIDER` allows.
13. **CloudFront origin ranges.** When `TK_ADDITIONAL_TRUSTED_PROXIES` is used, pass `-c additionalTrustedProxies=...` (see "Client addresses behind CloudFront") on every deploy so the ranges stay current. Outside us-west-2, pass `-c cloudFrontPrefixListId=<id>` for the region.
14. **Shared topology.** Optionally set `TK_PLATFORM_BOOTSTRAP_ADMIN_PASSWORD` in `<deploymentId>/<env>/platform` before the first start (left empty, the server logs a one-time invitation token for `platform.bootstrapAdminEmail`); the platform API is served on each platform host (`https://<platform host>/api/v1/platform/...`). Do steps 1, 2, and 10 for every property (each has its own zones, SES identity, and webhooks), the rest once for the deployment (secrets under `<deploymentId>/<env>/...`, GitHub environments on the deployment file's repository). Register each property in the backend's platform admin (MultiProperty mode) with its domains, app ids, media and updates base URLs (`https://media.<domain>`, `https://media.<domain>/updates`), cookie domain, and integration credentials (entered in the admin portal, never in Secrets Manager). Adding or removing a property changes the shared Api (certificate and alias record), Storage CORS, and GitHubDeploy stacks, so deploy `--all`.
15. **RecallDB (when `ai.recallDb` is on).** Nothing to fill: the database password, admin key, and token are generated. On the first deploy, check the `recalldb` log stream of the server log group for `listening on *:8600` and `recalldb-start: created the server credential`. If RecallDB cannot connect to RDS over TLS, its Npgsql version may require certificate trust settings that `RequireEncryption` does not set; report it before relying on vector knowledge. Docker Hub pulls are anonymous through the NAT gateways (rate limited); mirror the image into ECR and set `ai.recallDbImage` for production if pulls fail. Rotation: put a new value in `recalldb-token` or `recalldb-admin-key` and force new deployments of both the RecallDB and API services; for `recalldb-database`, force the API service first (db-bootstrap sets the password), then RecallDB.
16. **Cost allocation tag (dev and prod in one account).** In the Billing console, Cost allocation tags, activate the user-defined tag `Deployment` (it appears a day after the first tagged resources exist). Until then the tag-filtered budgets of a shared account report nothing.
17. **Directory geocoder and map (when `location` is on).** In the admin console (property settings, Directory geocoding and Directory map) set `directory.geocoder.provider` to `AwsLocation`, `directory.geocoder.placeIndexName` to the `PlaceIndexName` output, and `directory.geocoder.region` to the stack region (us-west-2); credentials come from the task role. For the map, create an Amazon Location API key out of band (`aws location create-key --key-name <slug>-<env>-map-key --restrictions AllowActions=geo:GetMap*,AllowResources=<map ARN>,AllowReferers=https://<domain>/*,https://www.<domain>/* --no-expiry`, then `aws location describe-key` for its value), set `directory.map.provider` to `MapLibre`, `directory.map.styleUrl` (and `darkStyleUrl`) to the `MapStyleUrl` output with `?key=<API key>` appended, and `directory.map.publicKey` to the key. Mobile apps send no referrer, so a referrer-restricted key works only on the website; use `directory.map.tileProxy` with `directory.map.proxyKey` for apps, or a second key restricted by app id. Nothing here is set by the stacks, so the console stays editable.
18. **Partio (when `partio.baseUrl` is set).** Put the Partio bearer token into `TK_PARTIO_API_KEY` of `<slug>/<env>/partio`, force a new ECS deployment, then turn on `documents.partio.enabled` in the admin console.

## Known limitations

- The server's settings file (`/app/config/trelliskit.json`) and `TK_STORAGE_DIRECTORY` live on the task's ephemeral disk. Settings saved from the admin console are lost when a task is replaced; environment variables and secrets win anyway. Use S3 for uploads.
- Account data exports are files on the task's ephemeral disk (`TK_STORAGE_DIRECTORY`, backend `AccountExportService`). With more than one task (prod runs at least two) the download may reach a task that does not hold the file, and a task replacement loses unexpired exports. Until the backend stores exports in S3 (or the service uses sticky sessions or a shared volume), treat exports as best effort in multi-task environments.
- The job template's poster capture takes the first frame (MediaConvert frame capture starts at 0 s), while the local ffmpeg processor uses 1 s. Validate one silent video once per account: the renditions read `Audio Selector 1`, and MediaConvert should fill missing audio with silence.
- In the shared topology, adding a property updates the shared API listener; reordering the deployment file's properties changes the listener's default certificate (harmless, because every API host is served by SNI).
- The ALB rule that refuses `CloudFront-Viewer-Address` on the public listener follows `ApiSettings.ViewerAddressHeader`, not the console. If an administrator changes the backend's viewer address header (`rest.viewerAddressHeader`) to another name, direct callers could send that header; leave the console value at its default (or set `ApiSettings.ViewerAddressHeader` to match and redeploy).
- Another account's CloudFront distribution can also reach the CloudFront origin listener (the prefix list covers all of CloudFront). Its `CloudFront-Viewer-Address` is still set by CloudFront to its real viewer, so it cannot forge an address; `X-Forwarded-Host` from it only selects a property, as below.
- `X-Forwarded-Host` on requests that reach the ALB directly is whatever the client sent (only the website distribution overwrites it). It only selects a property, never authorizes: tokens are bound to their property and `X-TK-Property` takes precedence.
- Platform hosts are routed to the API only. The platform console website (`/platform/*` of the website build) is not served on them yet; that needs its own distribution.
- The `db-bootstrap` container passes the application password to `psql` as a variable; PostgreSQL never logs it under the parameter group's settings (`log_statement` is off; `ALTER ROLE` is far below the slow query threshold).
- The bucket policies let CloudFront distributions of the whole account read through OAC (`AWS:SourceArn` `distribution/*`, see "OAC bucket grants"). When dev and prod share an account, a distribution of one environment could therefore be pointed at the other's buckets by hand; the stacks never do that. Separate accounts remove the possibility.
- In external DNS mode ACM stops validating a certificate whose validation record does not resolve within 72 hours (`VALIDATION_TIMED_OUT`), and the certificate checks then fail with that status. A redeploy does not request a new certificate for the same host names, so destroy the certificate stack (`$SLUG-<env>-Dns` or `$SLUG-<env>-EdgeCertificates`, possible while no other stack uses its certificates) and deploy it again. Create the validation records soon after step 1 to avoid this.
- WAF and Managed Grafana are not provisioned yet. Amazon Location Service is opt-in (`location`), and its map API key is created out of band (manual step 17).
- Offline lesson downloads do not work on AWS yet: the job template's download rendition waits for a backend change (see "Download rendition").
- `Content-Security-Policy` is off until the website's third-party origins are final (`WebSettings.ContentSecurityPolicy`).

## Cost estimate

Monthly, us-west-2 on-demand list prices, light launch traffic (hundreds of members). Usage-based items vary.

| Item | dev | prod |
|---|---|---|
| NAT gateways (hourly, before data) | 33 | 66 |
| Public IPv4 addresses (ALB and NAT) | 11 | 15 |
| Application Load Balancer | 20 | 25 |
| Fargate (server and sidecar) | 18 | 72 |
| RDS PostgreSQL (instance and gp3 storage) | 14 | 60 |
| CloudFront (3 distributions, mostly free tier) | 1 | 5 to 20 |
| S3 storage and requests (8 buckets; originals kept forever, so this grows with uploads) | 1 | 5 to 15 |
| Managed Prometheus, X-Ray, CloudWatch Logs, Container Insights, flow logs | 10 | 25 to 40 |
| Route 53 (1 zone plus one per vanity domain; none with external DNS), Secrets Manager (8, plus Mailgun when used), KMS (2 keys), ECR, one Lambda function (runs once) | 7 | 7 |
| RecallDB (only with `ai.recallDb`: Fargate 0.25 vCPU / 1 GB, namespace zone, 3 secrets) | 0 (11 when on) | 0 (11 when on) |
| Realtime bus metric (one CloudWatch custom metric and its embedded metric format logs) and alarm | under 1 | under 1 |
| Amazon Location (only with `location`: no fixed fee, per request; Storage intended use is priced higher than SingleUse) and Partio secret (0.40) | 0 (usage when on) | 0 (usage when on) |
| MediaConvert | usage | usage (about 0.015 USD per output minute per rendition) |
| **Total** | **about 115** | **about 280 to 320** |

An earlier 150 to 250 USD planning estimate predates the public IPv4 charge and counts one NAT gateway; dropping to one NAT gateway in prod saves about 35 USD. Budget alarms are set to 120 USD (dev) and 300 USD (prod) in `BudgetSettings`.
