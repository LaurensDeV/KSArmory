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
classes — so a comment can be both an insult and a threat.

**Each label has its own threshold, and `toxic` is effectively off.** That is
the important part, and it was measured rather than guessed. Taking the worst
label refuses ordinary frustration:

| | toxic | severe | obscene | threat | insult | ident |
| --- | --- | --- | --- | --- | --- | --- |
| a plain bug report | 0.001 | | | | | |
| "this mod is rubbish and the guidance never hits anything" | **0.83** | 0.00 | 0.13 | 0.00 | 0.17 | 0.00 |
| "garbage, total waste of time, broken junk" | **0.83** | 0.00 | 0.05 | 0.00 | 0.04 | 0.00 |
| "the dev is an idiot" | 0.98 | 0.03 | 0.71 | 0.00 | **0.94** | 0.02 |
| "worthless piece of garbage, I hope you die" | 0.99 | 0.27 | 0.51 | **0.85** | 0.70 | 0.06 |
| "I will find you and kill you" | 0.90 | 0.14 | 0.09 | **0.89** | 0.13 | 0.06 |

`toxic` fires at 0.83 on someone with a real bug being rude about the software.
The specific labels do not: insult stays under 0.17 and threat under 0.01 on
the same sentences, while abuse of a person reaches 0.94 and 0.89.

So the rule is **criticism of the mod is allowed however sharp, and abuse of a
person is not** — insult at 0.7, threat at 0.6, identity hate and severe at
0.5, obscene at 0.85, and `toxic` at 0.99 where it cannot act alone.

`MODERATION_API_KEY` still works and is used only when no local model is
present, so a deployment without the model baked in is not left unguarded.

**Both fail soft.** A classifier that throws, or an unreachable hosted
moderator, files the issue anyway with an `unmoderated` label. Failing closed
would put an optional dependency in charge of whether bug reports work at all;
failing open silently would let an outage publish anything unnoticed. The label
is how the difference stays visible.

**The log is judged too, and it is worth being clear why.** It looks like
machine output — `destroyed NewRocket_1`, `round 1 detonated` — but the part
after `destroyed` is a name a player chose. A slur in a craft name reaches a
public issue through the log and would walk straight past a gate that only
reads the summary.

It is judged on its condensed form: timestamps and levels dropped, numbers
collapsed, one of each distinct line kept. A log is the same handful of messages
repeated, so 12 KB becomes about 300 characters — and 12 KB scanned honestly
means eight model passes at nearly a second each, against one pass over the
whole thing that reads the first 512 tokens and silently ignores the rest.

**Each line is scored separately, not the log as a document.** One abusive line
among a dozen dull ones dilutes to nothing scored together: measured at `insult`
0.95 alone and 0.34 in company.

Failing this withholds the log and files the report anyway. A bug report is
still worth having without its attachment, and refusing the whole thing over a
craft name punishes the wrong part.

**A log that cannot be read through is withheld unread.** Condensing stops at
32 lines or 8 000 characters, which a real log never approaches and a log with
no newlines hits immediately. Publishing the part past the cut without scoring
it would be the one shape of this that fails open.

### This mod's vocabulary is violent, and that is fine

A weapons mod's bug reports say *kill*, *destroy*, *lethal*, *warhead*,
*blast*. That was measured against ten realistic reports and none is flagged:
threat and insult sit at essentially zero throughout, because the model
separates describing violence from directing it at a person.

The closest call is "I want to blow up a drone and nothing happens", at 0.775
`toxic` and 0.253 `threat` — comfortably under both thresholds, but it would
have been marginal under a single 0.8 rule on the worst label. Another reason
that rule is gone.

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
