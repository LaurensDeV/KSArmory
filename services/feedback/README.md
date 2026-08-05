# Feedback service

Takes a report from inside the game and files it as a GitHub issue. Runs at
`api.ksarmory.com`, behind Caddy on the VPS.

**Why a server at all:** a GitHub token shipped inside the mod is extractable
from the DLL in seconds, and it would be the maintainer's. Here it stays on the
server and the mod holds nothing worth stealing.

## The endpoint

```http
POST /feedback
Content-Type: application/json

{
  "kind": "bug",             // or "idea"
  "summary": "Turret will not elevate past 40 degrees",
  "detail": "Happens on a fresh craft, only after a reload.",
  "log": "<tail of KSArmory.log>",
  "modVersion": "0.8.9",
  "ksaVersion": "2026.8.5.5168",
  "platform": "Windows"
}
```

`202 Accepted` with the issue URL when one was filed, and **also** when the
report was declined as a duplicate or over the daily ceiling. That is
deliberate: a flooder learns nothing about which limit it hit, and someone
reporting a real bug is told it arrived either way.

`426 Upgrade Required` when the report comes from a version older than
`MIN_MOD_VERSION`, carrying the version needed so the mod can say which one to
get. A bug fixed two releases ago wastes triage, and the reporter has no way to
know that unless something tells them.

`400` for a malformed report, `429` when an address has run out of its hourly
allowance, `503` when the service has no token configured.

`GET /health` for the container.

## What stops abuse

An open endpoint that files public issues is a way to make the maintainer's
account publish whatever a stranger types. Each of these bounds a different
failure:

| Guard | Stops |
| --- | --- |
| Every field rendered inside a code fence | mentions notifying strangers, and markdown or HTML rendering as anything but text |
| Backticks replaced inside fenced text | closing the fence early to escape it |
| `Guard.StripInvisible` | bidi overrides and zero-width characters, which reorder or hide what is read |
| `Guard.ScrubPaths` | publishing the reporter's account name, which a KSA log path contains |
| Length caps, and a 64 KB body refused at the socket | one report filling a page |
| `Guard.LooksLikeMash` | keyboard mash arriving as an issue |
| Fingerprint plus a six hour window | one message sent a thousand times becoming a thousand issues |
| 5 reports per hour per address | one person being rude |
| **60 issues per day, service wide** | a botnet, where per-address limiting does nothing |
| Optional `FEEDBACK_SECRET` | casual scripts, and nothing more: the mod ships it, so it can be read out of the DLL |
| `MIN_MOD_VERSION` | reports against versions whose bugs are already fixed |
| OpenAI moderation, when a key is set | abuse and slurs reaching a public issue |

Version comparison is numeric per component, never lexical: `0.10.0` is newer
than `0.9.0` and a string comparison says the opposite. A missing or
unparseable version is refused rather than waved through, because a report that
cannot be placed against a build is worse than an old one.

The daily ceiling is the important one. Per-address rate limiting bounds a
person; only a global cap bounds "the repository has ten thousand issues by
morning".

**Counters are in memory**, so a restart resets them. That is a deliberate
trade: durable counters mean a database, and the ceiling exists to bound a
catastrophe rather than to be exact.

### Moderation

Everything above makes text *safe to render*. None of it says whether the text
is vile, which is a different question and not one a wordlist answers well.

With `MODERATION_API_KEY` set, the summary and detail go to OpenAI's moderation
endpoint (`omni-moderation-latest`, free with a key) before an issue is filed.
Flagged text gets `422` and a plain message, because a false positive on a real
bug report is possible and someone who is told can rewrite it.

Only what a person typed is sent. The log is machine output: sending it would
be pointless and a far larger disclosure than the reporter intended.

**It fails soft.** An unreachable moderator files the issue anyway, labelled
`unmoderated`. Failing closed would put someone else's availability in charge
of whether bug reports work at all; failing open silently would let an outage
quietly publish anything. A label says which issues were never checked, so they
can be looked at.

A report can still be rude or useless, and that is triage rather than
engineering. What it cannot be is dangerous to render.

## Configuration

| Variable | |
| --- | --- |
| `GITHUB_TOKEN` | fine-grained PAT, **Issues: write** on this repository only |
| `GITHUB_REPOSITORY` | `LaurensDeV/KSArmory` |
| `FEEDBACK_SECRET` | optional; when set, a report must carry the same string |
| `MIN_MOD_VERSION` | optional; oldest version accepted, e.g. `0.8.9`. Unset accepts any |
| `MODERATION_API_KEY` | optional; an OpenAI key. Unset skips the check entirely |

Scope the token to Issues on one repository. It is on a machine that accepts
requests from anyone, so it should be able to do exactly one thing.

## Running it

```bash
dotnet run                                  # local, on :5000
docker build -t ksarmory-feedback .
```

Deployment is a container on the VPS behind Caddy, which is configured in the
private infrastructure repository. `infra/README.md` explains the split: the
domain and the service live here, the machine lives there.
