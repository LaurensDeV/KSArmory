# SSH is the transport, not the deploy mechanism: the provider tunnels the Docker Engine API
# over it. Single-node Swarm, so a release is a service update rather than a new container.

provider "docker" {
  host = "ssh://${var.ssh_user}@${var.vps_host}"

  ssh_opts = [
    "-o", "StrictHostKeyChecking=accept-new",
    "-o", "IdentitiesOnly=yes",
    "-i", pathexpand(var.ssh_private_key),
  ]

  registry_auth {
    address  = "ghcr.io"
    username = var.ghcr_username
    password = var.ghcr_token
  }
}

# Overlay is required for Swarm services; attachable allows one-off containers.
resource "docker_network" "main" {
  name       = "ksarmory"
  driver     = "overlay"
  attachable = true
}
