# Only declare a hostname the VPS actually serves. Caddy answers on the names in
# its Caddyfile and nothing else, so a record without a matching site block
# resolves to a TLS handshake failure rather than a 404.

locals {
  # Proxied records are forced to automatic TTL by the API; sending anything
  # else is rejected.
  ttl = var.proxied ? 1 : var.ttl
}

resource "cloudflare_dns_record" "apex_a" {
  zone_id = var.zone_id
  name    = var.domain
  type    = "A"
  content = var.vps_ipv4
  ttl     = local.ttl
  proxied = var.proxied
}

resource "cloudflare_dns_record" "apex_aaaa" {
  count = var.vps_ipv6 == "" ? 0 : 1

  zone_id = var.zone_id
  name    = var.domain
  type    = "AAAA"
  content = var.vps_ipv6
  ttl     = local.ttl
  proxied = var.proxied
}

# CNAME rather than a second pair of address records: the apex is the only place
# the VPS address is written down, so a move is one change.
resource "cloudflare_dns_record" "www" {
  zone_id = var.zone_id
  name    = "www.${var.domain}"
  type    = "CNAME"
  content = var.domain
  ttl     = local.ttl
  proxied = var.proxied
}
