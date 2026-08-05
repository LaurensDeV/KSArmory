#!/usr/bin/env bash
#
# Runs the feedback service's tests -- the text rules that decide what a stranger's report is
# allowed to become before it is rendered on a public page.
#
# Needs no game assemblies and no model weights, which is the point: this runs on any machine and
# in the hosted CI job, unlike tools/test.sh. What it cannot cover is what the classifier scores;
# the weights are fetched during the image build, and those measurements live in
# services/feedback/README.md.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=env.sh
source "$REPO_ROOT/tools/env.sh"

dotnet test "$REPO_ROOT/tests/Feedback.Tests/Feedback.Tests.csproj" --nologo "$@"
