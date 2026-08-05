output "api_url" {
  description = "Public HTTPS URL the feedback endpoint is served on."
  value       = "https://${var.api_domain}"
}

output "deployed_image" {
  description = "Image digest currently running, or empty when the service is not deployed."
  value       = var.feedback_image
}
