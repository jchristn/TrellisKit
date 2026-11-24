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

## Preview and verification

Existing admin template previews and test sends remain available. `POST /api/v1/admin/email/test/catalog` takes an optional `to` address, sends each catalog template once using its sample values, and audits the operation once. It is restricted to administrators. Use a local SMTP inbox for broad catalog checks.

The `EmailBranding` suite covers neutral-property rendering, contrast, malformed layout files, property isolation, image sizing, Mailgun form fields, unsubscribe and tracking behavior, catalog authorization, and webhook variables. `EmailComposition` retains regressions for custom templates, layouts, image formats, and logo dimensions.

Mailgun delivery has no engine-provided exactly-once guarantee: if the provider accepts a request before a connection fails, an outbox retry may duplicate the message. Provider credentials and production delivery are configured separately from these property files.
