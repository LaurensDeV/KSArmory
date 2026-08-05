terraform {
  required_version = ">= 1.6"

  required_providers {
    cloudflare = {
      source  = "cloudflare/cloudflare"
      version = "~> 5.22"
    }
  }

  # State lives in R2 like the other infrastructure this domain shares a machine
  # with. Nothing here is secret, but a local state file is one laptop away from
  # records nobody can change.
  backend "s3" {
    key    = "ksarmory/dns.tfstate"
    region = "auto"

    use_lockfile = true

    skip_credentials_validation = true
    skip_metadata_api_check     = true
    skip_region_validation      = true
    skip_requesting_account_id  = true
    skip_s3_checksum            = true
  }
}

provider "cloudflare" {
  api_token = var.cloudflare_api_token
}
