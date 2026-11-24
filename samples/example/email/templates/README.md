# Email templates

Put `<key>.json` files here to replace an engine email template's default text and layout, or to add a new template. The format is in `docs/TRELLISKIT.md` section 12.2. The sample property ships none, so it uses the engine defaults.

Example `welcome.json`:

```json
{
  "subject": "Welcome to {name}",
  "heading": "Welcome, {memberName}",
  "blocks": [
    { "type": "body" },
    { "type": "list", "ordered": true, "items": ["Complete your profile.", "Say hello in the feed."] },
    { "type": "button" }
  ]
}
```
