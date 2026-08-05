# Caddy terminates TLS and renews Let's Encrypt certificates on its own. It reaches the service
# by name over the overlay network.

locals {
  # {uri} is a Caddy placeholder; OpenTofu only expands ${...}.
  caddyfile = <<-EOT
    {
      email ${var.acme_email}
    }

    ${var.api_domain} {
      reverse_proxy feedback:${var.feedback_port}
    }

    ${var.root_domain}, www.${var.root_domain} {
      redir ${var.project_url}{uri} permanent
    }
  EOT
}

# Swarm configs are immutable, so the name carries a content hash.
resource "docker_config" "caddy" {
  name = "ksarmory-caddyfile-${substr(sha256(local.caddyfile), 0, 12)}"
  data = base64encode(local.caddyfile)

  lifecycle {
    create_before_destroy = true
  }
}

# Holds the certificates and the ACME account. Losing it means re-issuing, and Let's Encrypt
# rate-limits that per domain per week.
resource "docker_volume" "caddy_data" {
  name = "caddy-data"
}

resource "docker_image" "caddy" {
  name         = "caddy:2-alpine"
  keep_locally = true
}

resource "docker_service" "caddy" {
  name = "caddy"

  # The upstream must exist before Caddy is asked to route to it.
  depends_on = [docker_service.feedback]

  task_spec {
    container_spec {
      image = docker_image.caddy.repo_digest

      configs {
        config_id   = docker_config.caddy.id
        config_name = docker_config.caddy.name
        file_name   = "/etc/caddy/Caddyfile"
      }

      mounts {
        target = "/data"
        source = docker_volume.caddy_data.name
        type   = "volume"
      }

      stop_grace_period = "10s"
    }

    networks_advanced {
      name = docker_network.main.id
    }
  }

  mode {
    replicated {
      replicas = 1
    }
  }

  update_config {
    order          = "stop-first" # a single replica binding :80 and :443
    failure_action = "rollback"
    parallelism    = 1
  }

  endpoint_spec {
    ports {
      target_port    = 80
      published_port = 80
      protocol       = "tcp"
      publish_mode   = "ingress"
    }
    ports {
      target_port    = 443
      published_port = 443
      protocol       = "tcp"
      publish_mode   = "ingress"
    }
  }

  converge_config {
    delay   = "7s"
    timeout = "3m"
  }
}
