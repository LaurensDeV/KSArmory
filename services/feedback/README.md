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
| A local toxicity classifier | abuse and slurs reaching a public issue |
| `Guard.LooksEnglish` | reports nobody triaging can read, and text an English model cannot score |

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

### English only

A report that is not English is refused with `422`. Two reasons, and the second
is the load-bearing one: everyone who triages these reads English, and the
classifier below is an English model, so scoring Dutch with it produces a
number that means nothing.

The test is deliberately lenient. Script is checked first, which is the
reliable half: a body of Cyrillic or CJK is not English at any length. Function
words are only counted when there are at least eight of them. **Short text is
always accepted** — "turret stuck" carries no evidence either way, and refusing
a real bug report for being terse is worse than reading the occasional one in
Dutch.

### Moderation

Everything above makes text *safe to render*. None of it says whether the text
is vile, which is a different question and not one a wordlist answers well.

**The classifier runs on this machine.** `unitary/toxic-bert` (Apache-2.0),
exported to ONNX during the image build **from the original weights** rather
than pulled from a re-upload of someone else's export. No key, no quota, no
account, nothing to sunset, and nothing anyone types leaves the box.

That last property is why it is worth the image size. Every hosted alternative
is a dependency that can be withdrawn: Perspective ran for nine years, served
Reddit and Wikipedia, and announced its own end date.

The head is **multi-label** — six independent sigmoids, not a softmax over six
classes — so a comment can be both an insult and a threat. The worst label is
compared against `CLASSIFIER_THRESHOLD`, which is a judgement rather than a
fact: 0.8 refuses abuse while letting through a report that calls the mod
rubbish, which someone with a genuine bug might well write.

`MODERATION_API_KEY` still works and is used only when no local model is
present, so a deployment without the model baked in is not left unguarded.

**Both fail soft.** A classifier that throws, or an unreachable hosted
moderator, files the issue anyway with an `unmoderated` label. Failing closed
would put an optional dependency in charge of whether bug reports work at all;
failing open silently would let an outage publish anything unnoticed. The label
is how the difference stays visible.

Only what a person typed is judged. The log is machine output: scoring it would
be pointless, and sending it anywhere is a far larger disclosure than the
reporter intended.

A report can still be rude or useless, and that is triage rather than
engineering. What it cannot be is dangerous to render.

## Configuration

| Variable | |
| --- | --- |
| `GITHUB_TOKEN` | fine-grained PAT, **Issues: write** on this repository only |
| `GITHUB_REPOSITORY` | `LaurensDeV/KSArmory` |
| `FEEDBACK_SECRET` | optional; when set, a report must carry the same string |
| `MIN_MOD_VERSION` | optional; oldest version accepted, e.g. `0.8.9`. Unset accepts any |
| `CLASSIFIER_DIR` | where `model.onnx` and `vocab.txt` live. `/app/model` in the image |
| `CLASSIFIER_THRESHOLD` | optional; refuse above this score. Defaults to `0.8` |
| `MODERATION_API_KEY` | optional OpenAI key, used only when no local model is present |

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
