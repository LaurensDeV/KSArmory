terraform {
  required_version = ">= 1.9"

  required_providers {
    docker = {
      source  = "kreuzwerker/docker"
      version = "~> 3.0"
    }
  }

  # Same R2 bucket as the DNS layer, different key. Nothing is shared between them but the
  # bucket: this layer knows the machine, that one knows the zone.
  backend "s3" {
    key    = "ksarmory/services.tfstate"
    region = "auto"

    skip_credentials_validation = true
    skip_metadata_api_check     = true
    skip_region_validation      = true
    skip_requesting_account_id  = true
    skip_s3_checksum            = true
    use_path_style              = true
  }
}
