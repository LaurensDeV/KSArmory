variable "cloudflare_api_token" {
  description = "Cloudflare token with Zone:Read and DNS:Edit on the zone below."
  type        = string
  sensitive   = true
}

variable "zone_id" {
  description = "Cloudflare zone ID, shown on the domain's overview page."
  type        = string
}

variable "domain" {
  description = "Apex domain this zone serves."
  type        = string
  default     = "ksarmory.com"
}

# Every name here needs a matching site block in the Caddyfile on the VPS. Caddy
# fails the TLS handshake for names it does not serve, so an unmatched record
# reads as a broken site rather than an absent one.
variable "subdomains" {
  description = "Hostnames served from the same machine as the apex."
  type        = set(string)
  default     = ["api"]
}

# Caddy answers the ACME HTTP-01 challenge on the VPS and holds the certificate.
# Proxying puts Cloudflare's certificate in front of that, so the origin needs
# Full (strict) or the site breaks with a redirect loop.
variable "proxied" {
  description = "Route through Cloudflare's proxy rather than DNS-only."
  type        = bool
  default     = false
}

variable "ttl" {
  description = "Record TTL in seconds. Must be 1 (automatic) when proxied."
  type        = number
  default     = 300
}

# Not a hostname, so the rule above about Caddy site blocks does not reach it: a
# TXT record is read by Discord rather than resolved to the machine, and there is
# nothing to serve. Public by construction -- anyone can dig it -- so it sits here
# rather than in the gitignored .env with the credentials.
#
# Kept after the domain is verified rather than removed. Discord re-reads it, and
# a zone that stops proving itself loses the link.
variable "discord_verification" {
  description = "Discord domain-verification token, without its dh= prefix. Empty skips the record."
  type        = string
  default     = "2cf4780881826cf6364d90635b7899c7cc7726d4"
}

# Given rather than read from the server's own state: that state belongs to a
# private repository, and this one is public. The address is written down twice
# as a result, which is the honest cost of not coupling them.
variable "vps_ipv4" {
  description = "Public IPv4 of the machine serving the site."
  type        = string
}

variable "vps_ipv6" {
  description = "Public IPv6 of the same machine. Empty skips the AAAA record."
  type        = string
  default     = ""
}
