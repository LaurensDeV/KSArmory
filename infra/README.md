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
| `ksarmory.com` | AAAA | `var.vps_ipv6`, omitted when empty — **and it is** |
| `www.ksarmory.com` | CNAME | `ksarmory.com` |
| `api.ksarmory.com` | CNAME | `ksarmory.com` |
| `_discord.ksarmory.com` | TXT | `dh=…`, proving the zone to Discord |

**`var.vps_ipv6` is deliberately empty.** The machine holds an IPv6 address, but
Docker's ingress network has `EnableIPv6=false` and `dockerd` binds `:80` and
`:443` on IPv4 only, so nothing accepts a connection on it. Publishing the AAAA
made every client that prefers IPv6 — which is most browsers on an IPv6
connection — fail or stall into a fallback, while `curl` from a v4 host said the
site was fine.

Setting it is not a DNS change on its own: Docker needs `ip6tables` enabled in
`daemon.json` and the `ingress` network recreated, which drops every service
while it happens.

Everything but the apex is a CNAME, so the VPS address appears once and moving
the site is one change rather than four. Add a name by putting it in
`var.subdomains`; `api` is there by default.

**The TXT record is the exception to the rule at the top of `dns.tf`**, which
says to declare only hostnames the VPS serves. Nothing resolves `_discord` to
the machine and there is no Caddy site block for it — Discord reads the value
and never connects. It stays after the domain is verified, because Discord
re-reads it and a zone that stops proving itself loses the link.

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

## Three things that will bite

**Port 22 is not reliably reachable from a GitHub runner.** Some runs cannot
open it at all — the SYN is dropped before the machine, which logs neither an
sshd connection nor a ufw block, while 80 and 443 are served throughout. It is
not the host: ufw allows 22 from anywhere, there is no fail2ban, sshguard or
CrowdSec, conntrack sits near empty and the kernel reports no SYN flooding or
listen-queue overflow. Runs that do connect come from the same Azure ranges as
runs that cannot, so it is neither the source address nor the key.

Nothing at either end can see the hop that drops it, and it clears on its own.
So `deploy.yml` waits for port 22 before applying and retries the apply, rather
than losing a release to a transient. A deploy that fails every attempt is a
different fault and worth reading the log for.

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
