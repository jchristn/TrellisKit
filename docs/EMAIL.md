# Email delivery and property layouts

TrellisKit composes every catalog message as HTML and plain text, stores an encrypted-token outbox record, and retries delivery with bounded exponential backoff. Templates remain property-neutral. A property can replace copy through `email/templates/<key>.json` and presentation through the optional `email/layout.json`.

## Mailgun transport

`MailgunEmailSender` posts form fields directly to the configured Mailgun messages API so both `text` and `html`, `h:Reply-To`, custom headers, tags, and correlation variables survive delivery. SendWithMailgun remains the address-validation provider. Reserved addressing and MIME headers cannot be overridden through custom headers; names with control characters are rejected and values are folded onto one line.

Each send includes the template key and category as tags and `tk-message-id`, `tk-property-id`, and `tk-template` variables. Signature-checked webhooks first correlate by provider message id; if that does not match, `tk-message-id` can identify the property-scoped record only when the event recipient matches. A missing or different recipient does not authorize variable-based correlation.

Optional messages with the signed unsubscribe token carry `List-Unsubscribe` and `List-Unsubscribe-Post`. Essential broadcasts explicitly opt out. Property settings `email.trackOpens` and `email.trackClicks` default to false. Only Optional messages can enable tracking; Essential and Staff sends explicitly disable tracking so their links remain unchanged.

No live Mailgun requests are needed for the test suite. The recording HTTP handler verifies request forms and provider responses; isolated server tests cover queueing, suppression, tracking, headers, admin authorization, and webhook correlation.

## Property layout

All fields in `email/layout.json` are optional. Missing files preserve neutral defaults; invalid fields are ignored and logged. Files support JSON comments and trailing commas.

| Field | Meaning |
|---|---|
| `logo`, `logoDark` | Existing files directly under `brand/`; default light logo is `property.json` `brand.logo`. The dark variant is optional. |
| `logoWidth`, `logoHeight` | Explicit CSS dimensions, both required to override automatic sizing; width 16 to 240, height 16 to 96. Without them, PNG, GIF, and JPEG dimensions are fitted inside 200 by 48 without enlargement or distortion. Unknown dimensions use automatic width. |
| `canvas`, `card`, `heading` | Light background and heading colors as `#RRGGBB`. |
| `canvasDark`, `cardDark` | Dark backgrounds as `#RRGGBB`. |
| `accentBar` | Optional decorative color across the card's top. |
| `footerNote` | One line, up to 200 characters; defaults to the property tagline. |
| `copyright` | One line, up to 200 characters; `{year}` uses the current UTC year. Defaults to the property owner. |
| `postalAddress` | One line, up to 300 characters. The `email.postalAddress` runtime setting takes precedence. |

```json
{
  "logo": "brand/logo.png",
  "canvas": "#F4F5F7",
  "card": "#FFFFFF",
  "accentBar": "#1F5FBF",
  "footerNote": "Learn together",
  "copyright": "(c) {year} Example Owner. All rights reserved."
}
```

The layout uses presentation tables, a padded hidden preheader, a table-cell button, the property's language, logo alt text, light/dark colors, and privacy, terms, and preferences links. Text colors are checked against backgrounds at 4.5:1 and replaced with readable fallbacks where needed. The existing `EmailBrand.LogoSize` API remains supported for custom callers.

## Staff email details

Staff templates (category `Staff`) render a `details` token after the body: titled tables built by `StaffEmailDetailsService` and serialized by `StaffEmailDetails` (at most 8 sections of 20 rows, labels clipped to 120 characters and values to 400). An administrator reading a new sign-up, report, ticket, or subscription email sees who did it and from where without opening the console.

| Section | Shown on | Rows |
|---|---|---|
| Request | Emails a member request triggered: `staff_new_member`, `staff_new_report`, `staff_ticket_opened`, `staff_directory_submission`, `staff_event_proposal` | IP address (resolved like request history), client, platform (`X-Client-Platform`), app version (`X-Client-Version`), browser and operating system (`UserAgentSummary`), time in the property's `usage.timeZone` and UTC, request id linked to `/admin/requests?id=` |
| Member | Every staff email about a member, titled Member, Reporter, Author, Requester, Submitter, or Proposer | Name, handle, email and verification, account id, sign-up method and linked methods, created time and age, city/region/country, age attested, roles, membership, sign-up code, restrictions, admin link |
| Sign-up | `staff_new_member` | Accounts created from the same IP in the last 24 hours (from `account.signup` audit records), documents accepted |
| Report, Content | `staff_new_report`, `staff_content_auto_hidden` | Report reason, details, scope, and review due time; content type, id, scope, title, excerpt (300 characters), state, open reports; author 90-day history (removals, hides, reports against); reporter reports filed in 90 days; for automatic hides, Reports (distinct reporters, hide threshold, reasons) |
| Overdue reports | `staff_moderation_overdue` | Oldest overdue report's age, counts by scope and by reason |
| Subscription | `staff_new_subscription`, `staff_payment_failure`, `staff_cancellation`, `staff_refund` | Plan, product id, list price, store and environment, store subscription and transaction ids, status and auto-renew changes, current and previous period |
| Ticket | `staff_ticket_opened`, `staff_ticket_overdue` | Type, priority, status, first 300 characters of the body, the client the ticket recorded; age, first response due, and assignee when overdue |
| Submission, Proposal | `staff_directory_submission`, `staff_event_proposal` | The submission or proposal and the submitter's 90-day history with the share accepted |
| Origin | Mail no member request triggered (store notifications, workers, jobs, monitors); templates declare it with `EmailTemplate.StaffOrigin` | Source, site, server version, trace id when one exists. Never an IP address |

The property setting `email.staffRequestDetails` (default `true`) removes the Request section; member details always show. No location is derived from an IP address.

The queue drops `details` from every non-staff template, so member mail never carries it, and `EmailTemplate.Compose` and property JSON templates render it only for the `Staff` category. A property JSON template places the sections with a `{ "type": "details" }` block; without one they are added after the last block. Values are literal text: they are HTML-encoded and braces never fill placeholders. Each staff template has a details sample (`StaffEmailSamples`) for admin previews and catalog test sends. The `StaffEmailDetails` suite covers sign-up, report, and subscription details, the setting, member-template isolation, and malformed input.

## Preview and verification

Existing admin template previews and test sends remain available. `POST /api/v1/admin/email/test/catalog` takes an optional `to` address, sends each catalog template once using its sample values, and audits the operation once. It is restricted to administrators. Use a local SMTP inbox for broad catalog checks.

The `EmailBranding` suite covers neutral-property rendering, contrast, malformed layout files, property isolation, image sizing, Mailgun form fields, unsubscribe and tracking behavior, catalog authorization, and webhook variables. `EmailComposition` retains regressions for custom templates, layouts, image formats, and logo dimensions.

Mailgun delivery has no engine-provided exactly-once guarantee: if the provider accepts a request before a connection fails, an outbox retry may duplicate the message. Provider credentials and production delivery are configured separately from these property files.
