# ksarmory.com is the wiki, a Cloudflare Pages site, and www is the same site. The project, its two domains
# and the records pointing at it are declared together so none can move without the others;
# .github/workflows/wiki.yml uploads the site into the project.

resource "cloudflare_pages_project" "site" {
  account_id        = var.account_id
  name              = var.pages_project
  production_branch = "main"
}

resource "cloudflare_pages_domain" "site" {
  for_each = toset([var.domain, "www.${var.domain}"])

  account_id   = var.account_id
  project_name = cloudflare_pages_project.site.name
  name         = each.key
}

# Proxied, unlike the VPS's records: Pages terminates TLS itself and serves a custom domain through
# Cloudflare whichever way the record is set. A CNAME at the apex is flattened by Cloudflare.
resource "cloudflare_dns_record" "site" {
  for_each = toset([var.domain, "www.${var.domain}"])

  zone_id = var.zone_id
  name    = each.key
  type    = "CNAME"
  content = cloudflare_pages_project.site.subdomain
  ttl     = 1
  proxied = true
}

# The apex's A record and www's CNAME became these. Moved rather than left to be destroyed beside them,
# because a name cannot hold a CNAME and an A record at once: as a move, the apex's type change replaces
# the record, removing the old before creating the new.
moved {
  from = cloudflare_dns_record.apex_a
  to   = cloudflare_dns_record.site["ksarmory.com"]
}

moved {
  from = cloudflare_dns_record.www
  to   = cloudflare_dns_record.site["www.ksarmory.com"]
}
