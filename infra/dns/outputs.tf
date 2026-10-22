output "domain" {
  description = "Apex domain: the site, on Cloudflare Pages."
  value       = var.domain
}

output "hostnames" {
  description = "Every name the VPS serves, for cross-checking against the Caddyfile."
  value       = [for s in var.subdomains : "${s}.${var.domain}"]
}

output "site" {
  description = "The Pages project the apex and www serve."
  value = {
    project = cloudflare_pages_project.site.name
    pages   = cloudflare_pages_project.site.subdomain
  }
}
