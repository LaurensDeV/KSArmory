# ksarmory.com and www are the site, on Cloudflare Pages (site.tf). Everything the VPS serves is a CNAME to
# vps.ksarmory.com, which alone carries its address: Caddy answers on the names in its Caddyfile and nothing
# else, so declare a name here only once it has a site block there.

locals {
  # Proxied records are forced to automatic TTL by the API; sending anything
  # else is rejected.
  ttl = var.proxied ? 1 : var.ttl
}

resource "cloudflare_dns_record" "vps_a" {
  zone_id = var.zone_id
  name    = "vps.${var.domain}"
  type    = "A"
  content = var.vps_ipv4
  ttl     = local.ttl
  proxied = var.proxied
}

resource "cloudflare_dns_record" "vps_aaaa" {
  count = var.vps_ipv6 == "" ? 0 : 1

  zone_id = var.zone_id
  name    = "vps.${var.domain}"
  type    = "AAAA"
  content = var.vps_ipv6
  ttl     = local.ttl
  proxied = var.proxied
}

# CNAMEs rather than more address records: vps is the only place the VPS address is written down, so a move
# is one change.
resource "cloudflare_dns_record" "subdomain" {
  for_each = var.subdomains

  zone_id = var.zone_id
  name    = "${each.key}.${var.domain}"
  type    = "CNAME"
  content = cloudflare_dns_record.vps_a.name
  ttl     = local.ttl
  proxied = var.proxied
}

# var.ttl rather than local.ttl: that local exists to force automatic TTL on a
# proxied record, and a TXT record is never proxied -- Cloudflare rejects it.
resource "cloudflare_dns_record" "discord_verification" {
  count = var.discord_verification == "" ? 0 : 1

  zone_id = var.zone_id
  name    = "_discord.${var.domain}"
  type    = "TXT"
  content = "dh=${var.discord_verification}"
  ttl     = var.ttl
}
