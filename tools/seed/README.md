# Demo data seeder

Fills a running TrellisKit stack with realistic demo content from a seed file that the property owns (for example `<property>/seed/demo.json`). It talks only to the public REST API, as the break-glass admin token and as the member accounts it creates, so every object goes through the same code paths as the website and the apps (registration, email verification through Mailpit, uploads, media processing, rate limits, audits, notifications).

The tool is engine-generic: it knows nothing about any property. Names, ranks, reactions, categories, programs, and text all come from the seed file and the property configuration. The engine's sample property has a small seed file at `samples/example/seed/demo.json`.

Requirements: Node 24 (no npm packages), a running stack (see `docker/README.md`), and, for verification and password reset emails, the stack's Mailpit. `generate-assets.mjs` needs ffmpeg on the host or a running container that has it (the local stack's server image does).

## Quick start

```bash
cd <engine>/tools/seed
node generate-assets.mjs --file <property>/seed/demo.json      # once: creates the placeholder images and clips
TK_ADMIN_TOKEN=<local admin token> node seed.mjs --file <property>/seed/demo.json
```

The admin token is `auth.adminToken` in the stack's settings (`docker compose exec server cat /app/config/trelliskit.json`). A run prints a summary of what it created or found, any API gaps it hit, and warnings, and writes every generated account and its random local password to the credentials file named in the seed file (default `ACCOUNTS.local.md` next to it). That file must stay git-ignored.

| Option | Default | Meaning |
|---|---|---|
| `--file <path>` | required | The seed file |
| `--api <url>` | `http://127.0.0.1:8000` (`TK_SEED_API`) | API base URL |
| `--web <url>` | `http://127.0.0.1:8080` (`TK_SEED_WEB`) | Website URL written to the credentials file |
| `--mailpit <url>` | `http://127.0.0.1:8025` (`TK_SEED_MAILPIT`) | Mailpit for verification and reset links; `none` skips email verification |
| `--admin-token <token>` | `TK_ADMIN_TOKEN` | Break-glass admin token |
| `--app <bundle id>` | `TK_SEED_APP` | Registered app id sent as `X-TK-App`; required in `MultiProperty` (`deployment.requireAppHeader`) |
| `--property <id>` | `TK_SEED_PROPERTY`, else the seed file's `propertyId` when `--app` is set | Property id sent as `X-TK-Property` (`MultiProperty`) |
| `--only a,b` / `--skip a,b` | all | Steps: `codes`, `accounts`, `forums`, `feed`, `threads`, `directory`, `events`, `library`, `messages`, `support`, `ideas`, `broadcasts`, `ai` (codes and accounts always run, since everything else needs the sessions) |
| `--reset-content` | | Remove the seeded content instead of creating it (below) |
| `--validate` | | Check the seed file offline and exit |
| `--verbose` | | More logging |

## Idempotency

Every object the tool creates carries the tag `seed: <key>` (the namespace is the seed file's `namespace`, default `seed`). Before creating anything the tool looks the key up through the matching enumerate route (admin or member), so running it again creates only what is missing. Accounts are matched by email; when the stored password no longer works (for example after the credentials file was deleted) the tool resets it through the password reset email in Mailpit. Comments are tagged `<object key>/<comment key>`, direct messages are matched by author and text, reactions, RSVPs, votes, and progress are naturally idempotent or skipped when present. The tool works against a fresh database or one that already holds the seeded content.

## `--reset-content`

Removes every object carrying the seed tag: feed posts, threads, forums, venues, instructors, series, lessons, ideas, tickets (closed, then deleted), AI assistants and endpoints, and the seeded members' conversations (deleted for both members). Objects the API cannot delete are handled the closest way it allows: published events are canceled (without notifying attendees) and re-tagged `seed-reset` so the next run creates fresh ones, scheduled broadcasts are canceled, sign-up codes are deactivated (the next run reactivates them), and pending directory submissions and event proposals are withdrawn by their submitter. Sent broadcasts stay (there is no delete route for them). Accounts, memberships, and staff roles are kept.

## Seed file

```jsonc
{
  "schemaVersion": 1,
  "propertyId": "app_...",          // optional: refuse to run against another property
  "namespace": "seed",              // tag key that marks seeded objects
  "accounts": { "emailDomain": "demo.example.test", "credentialsFile": "ACCOUNTS.local.md", "adminMember": "admin" },
  "defaults": { "reactionKeys": ["like", "heart"] },
  "signupCodes": [ { "key", "code", "name", "grantType", "grantMonths", "maxRedemptions", "labels" } ],
  "members": [ { "key", "displayName", "handle", "bio", "profile": { /* property profile field keys */ },
                 "avatar": "<asset key>", "membership": { "type": "comp" | "code" | "none", "code": "<code key>", "months": 12 },
                 "flags": { "canPostToFeed": true }, "roles": [ { "role": "SystemAdmin" } ],
                 "grants": [ { "resourceType": "Feed" | "Forum" | "Events" | "Library" | "Directory", "resourceId": "*" } ], "notes" } ],
  "forums": [ { "key", "name", "slug", "description", "allowedThreadTypes", "allowVideo" } ],
  "feed": [ { "key", "author", "title", "body", "media": ["<asset key>"], "audience", "pin", "labels", "tags",
              "autoReactions": { "count": 10, "keys": ["like"] },
              "comments": [ { "key", "author", "body", "replies": [ ... ], "report": { "by", "reason", "details" } } ] } ],
  "threads": [ { "key", "forum", "author", "type": "Discussion" | "Question" | "Poll" | "Feedback", "title", "body",
                 "poll": { "options": [], "closesInDays": 14 }, "votes": { "0": ["<member>"] },
                 "comments": [ ... ], "acceptAnswer": "<comment key>", "review": { "by", "comment", "status": "Reviewed" } } ],
  "venues": [ { "key", "name", "address", "geo": { "lat", "lng" }, "programs", "photos": ["<asset key>"], "verified" } ],
  "instructors": [ { "key", "displayName", "rank", "rankLevel", "venues": [ { "venue", "role" } ], "linkMember" } ],
  "directorySubmissions": [ { "key", "submitter", "type": "NewVenue", "fields": { } } ],
  "venueReviews": [ { "key", "author": "<member key>", "venue": "<venue key>", "stars": 1-5, "text", "labels", "tags",
                     "reply": { "text", "author": "<member key>" } } ],
  "venueMedia": [ { "key", "member": "<member key>", "venue": "<venue key>", "asset": "<asset key>", "labels", "tags" } ],
  "events": [ { "key", "creator", "title", "category", "format", "timeZone",
                "start": { "inDays": 3, "time": "18:00" } /* or { "weekday": "Sat", "time": "11:00" } */, "durationMinutes",
                "venue", "capacity", "waitlistEnabled", "recurrence", "hosts", "rsvps": { "going": [], "interested": [] }, "comments" } ],
  "eventProposals": [ { "key", "proposer", "title", "start", "durationMinutes", "proposalNote" } ],
  "library": { "editor", "series": [ { "key", "title", "cover", "levels", "lessons": [ { "key", "title", "video", "kind", "audience" } ] } ],
               "progress": [ { "member", "lesson", "complete": true } ], "comments": [ { "lesson", "key", "author", "body" } ] },
  "conversations": [ { "key", "from", "to", "messages": [ { "from", "body" } ] } ],
  "tickets": [ { "key", "requester", "type", "title", "body", "state", "replies": [ { "by", "body" } ], "publishIdea": { "ideaKey" } } ],
  "ideas": [ { "key", "creator" | "fromTicket", "title", "body", "status", "statusNote", "voters": [] } ],
  "broadcasts": [ { "key", "creator", "action": "send" | "schedule", "sendInHours", "title", "inApp", "email", "channels", "segment" } ],
  "ai": { "leaveEnabled": false, "endpoints": [ { "key", "kind", "apiType", "model", "fallbackModels", "endpointUrl" } ],
          "assistants": [ { "key", "name", "chat": "<endpoint key>", "embedding": "<endpoint key>", "knowledge", "tools" } ] },
  "assets": [ { "key", "kind": "image" | "video", "style": "avatar", "file": "assets/x.jpg", "title", "subtitle", "background", "durationSeconds" } ]
}
```

Text fields accept `{@memberKey}` (or `{@handle}`), which becomes the member's `@handle` mention. Times are relative to the run, so a seed file stays useful on any day. Members referenced as authors need the access the content needs (membership in gated models, `canPostToFeed` for feed posts, the staff role for staff actions). `--validate` checks references and duplicate keys.

## Notes

- Rate limits are honored: a `429` waits for `Retry-After` and retries. New accounts have tighter comment, thread, and messaging limits, so the first run takes a few minutes.
- The AI admin routes answer only while the property's AI switch is on. The `ai` step turns it on, configures endpoints and assistants, then leaves it as `ai.leaveEnabled` says (default off). Knowledge indexing runs once AI is switched on.
- Venue reviews are written by their `author` (one per member per venue; an existing review of that member is kept) and tagged with the seed key. The optional `reply` is posted by `reply.author`, or else by the `linkMember` of an instructor entry linked to the same venue (only linked instructors may reply). `venueMedia` items are uploaded by their `member` through the media pipeline and attached to the venue's Media section. `--reset` deletes both as their authors.
- Usage metering needs no seeding: the usage worker rolls the new activity up within `usage.rollupIntervalMinutes`.
- `generate-assets.mjs --file <seed> [--force] [--container <name>] [--only key,key]` writes simple original placeholder art: colored cards with text, initials avatars, and short clips with a moving bar and a silent audio track.
