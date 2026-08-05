# Infrastructure

`ksarmory.com` and the machine behind it, as code. OpenTofu, two layers, one
R2 bucket for state.

| Layer | State key | What it owns |
| --- | --- | --- |
| `infra/dns` | `ksarmory/dns.tfstate` | the Cloudflare records |
| `infra/services` | `ksarmory/services.tfstate` | Caddy and the feedback service on the VPS |

The split is by change cadence, not by subject: records change when a hostname
is added, services change on every deploy of the image.

## Applying

Each layer takes the same two files, both gitignored and neither belonging in a
commit:

```bash
cd infra/<layer>
cp .env.example .env               # what this layer needs
cp backend.hcl.example backend.hcl # R2 credentials for the state
chmod 600 .env backend.hcl

set -a && source .env && set +a
tofu init -backend-config=backend.hcl
tofu plan
```

The Cloudflare token needs **Zone:Read** and **DNS:Edit**, scoped to this zone
alone: *My Profile → API Tokens → Create Token → Edit zone DNS*. An R2 token is
not one of these — it can read the state bucket and sees no zones at all, which
the API reports as an empty list rather than an error.

## Records

| Name | Type | Points at |
| --- | --- | --- |
| `ksarmory.com` | A | `var.vps_ipv4` |
| `ksarmory.com` | AAAA | `var.vps_ipv6`, omitted when empty |
| `www.ksarmory.com` | CNAME | `ksarmory.com` |
| `api.ksarmory.com` | CNAME | `ksarmory.com` |

Everything but the apex is a CNAME, so the VPS address appears once and moving
the site is one change rather than four. Add a name by putting it in
`var.subdomains`; `api` is there by default.

## Services

`api.ksarmory.com` serves the feedback endpoint. The apex and `www` redirect to
the project page, there being no site to serve yet.

The image is built by `.github/workflows/feedback-image.yml` and deployed **by
digest**, so a moved tag cannot silently change what runs. `var.feedback_image`
empty means the service is not deployed, and Caddy then routes to nothing —
set it to the digest that workflow prints.

Caddy holds `:80` and `:443` as a single replica with a `stop-first` update, so
a bad Caddyfile takes the site down rather than rolling back. Render it before
applying:

```bash
tofu console <<<'local.caddyfile'
```

`caddy-data` carries the certificates and the ACME account. Deleting that volume
means re-issuing, and Let's Encrypt rate-limits per domain per week.

## Two things that will bite

**Records are DNS-only, not proxied.** Caddy answers the ACME HTTP-01 challenge
on the origin and holds the certificate. Turning Cloudflare's proxy on puts a
second TLS terminator in front of that, and unless the origin is set to **Full
(strict)** the result is a redirect loop rather than an error. `var.proxied`
flips it once that is configured; the TTL is forced to automatic when it is,
because the API rejects anything else.

**A record without a matching Caddy site block is worse than no record.** Caddy
serves the names in its Caddyfile and fails the TLS handshake for everything
else, so a name that resolves but is not configured looks like a broken site
rather than an absent one. The two layers are applied separately, so this is a
real window rather than a theoretical one — but DNS has to exist first, because
Caddy cannot pass an ACME challenge for a name that does not resolve.
