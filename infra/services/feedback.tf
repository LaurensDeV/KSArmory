# Takes bug reports from inside the mod and files them as GitHub issues. Built by
# .github/workflows/feedback-image.yml and pulled by digest.

resource "docker_image" "feedback" {
  count = var.feedback_image == "" ? 0 : 1

  name         = var.feedback_image
  keep_locally = false
  force_remove = true
}

resource "docker_service" "feedback" {
  count = var.feedback_image == "" ? 0 : 1

  name = "feedback" # Swarm DNS name Caddy routes to

  task_spec {
    container_spec {
      image = docker_image.feedback[0].repo_digest

      env = {
        ASPNETCORE_URLS   = "http://+:${var.feedback_port}"
        GITHUB_REPOSITORY = var.github_repository
        GITHUB_TOKEN      = var.github_token
        MIN_MOD_VERSION   = var.min_mod_version

        # The health check runs every 30s and ASP.NET logs eight lines per request at
        # Information, which is ~23,000 lines a day about nothing. The service's own messages
        # are logged under its category and are unaffected.
        Logging__LogLevel__Microsoft                  = "Warning"
        Logging__LogLevel__Microsoft_Hosting_Lifetime = "Information"
      }

      stop_grace_period = "10s"

      # There is no curl or wget in the image; the service answers its own /health.
      healthcheck {
        test         = ["CMD", "dotnet", "/app/Feedback.dll", "--healthcheck"]
        interval     = "30s"
        timeout      = "5s"
        retries      = 3
        start_period = "40s"
      }
    }

    networks_advanced {
      # ID, not name: the API returns the ID, so .name leaves a permanent diff.
      name = docker_network.main.id
    }

    # The classifier is a few hundred MB resident and inference is CPU-bound. Unbounded it
    # competes with everything else on a two-core box.
    resources {
      limits {
        memory_bytes = 1024 * 1024 * 1024
      }
    }
  }

  mode {
    replicated {
      replicas = 1
    }
  }

  update_config {
    order          = "start-first"
    failure_action = "rollback"
    parallelism    = 1
  }

  converge_config {
    delay   = "7s"
    timeout = "6m" # a cold pull of this image dominates the first converge
  }
}
