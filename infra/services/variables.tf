variable "vps_host" {
  description = "Address of the machine this runs on. The DNS layer points the zone at it."
  type        = string
  default     = "45.133.178.13"
}

variable "ssh_user" {
  description = "User the Docker provider reaches the daemon as."
  type        = string
  default     = "root"
}

variable "ssh_private_key" {
  description = "Path to the private key authorised on that machine."
  type        = string
  default     = "~/.ssh/id_rsa"
}

variable "feedback_image" {
  description = "Immutable image for the feedback service, by digest. Empty means not deployed."
  type        = string
  default     = ""
}

variable "feedback_port" {
  description = "Port the feedback service listens on inside the container."
  type        = number
  default     = 8080
}

# The KSA Multiplayer directory: the list of servers the mod's browser reads. Its container belongs
# to that repository's own state, which cannot edit this Caddyfile, so the route is here and the
# container is there. Caddy reaches it by name over this overlay network, as it reaches feedback.
#
# Unproxied at Cloudflare like every other name on this machine, which costs nothing here: the list
# is a few hundred reads a day, so there is no caching worth a second TLS terminator in front of
# Caddy.
#
# Two names for one list: the multiplayer mod's own domain, which is where its games are pointed,
# and this one, which older games have compiled in as the place to ask when nothing else answers.
# Caddy takes a site's names separated by commas and holds a certificate for each.
variable "servers_domain" {
  description = "Where the KSA Multiplayer server directory is served: one name, or several separated by commas."
  type        = string
  default     = "servers.ksarmory.com, servers.ksamp.gg"
}

variable "directory_port" {
  description = "Port the directory listens on inside its container."
  type        = number
  default     = 8080
}

variable "api_domain" {
  description = "Where the feedback endpoint is served."
  type        = string
  default     = "api.ksarmory.com"
}

variable "acme_email" {
  description = "Contact address for Let's Encrypt expiry notices."
  type        = string
  default     = "laurens@devoogd.be"
}

variable "ghcr_username" {
  description = "GitHub username used to pull the image from GHCR."
  type        = string
  default     = ""
}

variable "ghcr_token" {
  description = "GitHub token with read:packages."
  type        = string
  default     = ""
  sensitive   = true
}

variable "github_repository" {
  description = "Repository the service files reports in."
  type        = string
  default     = "LaurensDeV/KSArmory"
}

variable "github_token" {
  description = "Fine-grained PAT with Issues:write on that repository alone."
  type        = string
  default     = ""
  sensitive   = true
}

variable "min_mod_version" {
  description = "Oldest mod version accepted. Empty accepts any."
  type        = string
  default     = ""
}
