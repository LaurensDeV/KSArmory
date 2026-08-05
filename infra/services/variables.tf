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

variable "api_domain" {
  description = "Where the feedback endpoint is served."
  type        = string
  default     = "api.ksarmory.com"
}

variable "root_domain" {
  description = "Apex. Redirected to the project page: there is no site to serve yet."
  type        = string
  default     = "ksarmory.com"
}

variable "project_url" {
  description = "Where the apex and www redirect to."
  type        = string
  default     = "https://github.com/LaurensDeV/KSArmory"
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
