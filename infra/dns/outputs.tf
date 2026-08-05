output "domain" {
  description = "Apex domain served from the VPS."
  value       = var.domain
}

output "hostnames" {
  description = "Every name declared here, for cross-checking against the Caddyfile."
  value       = [var.domain, "www.${var.domain}"]
}
