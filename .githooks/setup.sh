#!/bin/bash
#
# Run this once after cloning the repo:
#   ./.githooks/setup.sh
#
# Points git at the in-repo hooks instead of .git/hooks (which isn't
# version-controlled). Idempotent — safe to re-run.

set -e

git config core.hooksPath .githooks

cat <<EOF
✓ core.hooksPath set to .githooks
  - pre-commit  → auto-format per workspace touched
  - pre-push    → build + test per workspace touched

To bypass in a real emergency: git commit/push --no-verify
(do not make a habit of it; CI will catch you).
EOF
