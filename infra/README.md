# Infrastructure

`ksarmory.com`, as code. OpenTofu, one layer, no server of its own.

The site is served by a VPS this repository does not manage: Caddy on that
machine terminates TLS, holds the Let's Encrypt certificate, and reverse-proxies
to a container built from `site/`. **What lives here is the domain and the thing
being served; what lives there is the machine.**

That split is deliberate. Two repositories cannot both own one Caddyfile, and
this one is public while the server's is not.

| Here | There |
| --- | --- |
| `infra/dns` — the Cloudflare records | the VPS, Docker Swarm, Caddy |
| `site/` — what is served, published to GHCR | a `site_image` value naming the tag to run |

## Applying

```bash
cd infra/dns
cp .env.example .env               # Cloudflare token, zone id, VPS addresses
cp backend.hcl.example backend.hcl # R2 credentials for the state
chmod 600 .env backend.hcl

set -a && source .env && set +a
tofu init -backend-config=backend.hcl
tofu plan
```

Both files are gitignored. Neither belongs in a commit.

The Cloudflare token needs **Zone:Read** and **DNS:Edit**, scoped to this zone
alone: *My Profile → API Tokens → Create Token → Edit zone DNS*. The zone ID is
on the domain's overview page.

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
rather than an absent one. Add the record and the site block together.

## The VPS address is written down twice

Once here, once in the repository that provisions the machine. That layer
exposes it as state output and this one could read it, which would remove the
duplication and add a dependency on a private repository's state bucket and key.

Given this repository is public and the address changes roughly never, the
duplication is the cheaper of the two.
