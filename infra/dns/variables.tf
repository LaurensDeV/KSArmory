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
