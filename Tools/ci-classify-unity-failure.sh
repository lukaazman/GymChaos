#!/usr/bin/env bash
# Classifies a failed game-ci unity-builder step from the Unity log it wrote.
# Prints "infra=true" when the failure is not the verifier's or the compiler's:
# license activation, a Unity crash (exit 134/139), a lost container, no log at all.
# Prints "infra=false" when the log carries a verifier verdict (GYMCHAOS_*_FAIL*)
# or a C# compile error: that failure is real and must not be retried.
set -u
log="${1:-unity-ci.log}"
infra=true
# GYMCHAOS_ROAM_ROUTE_FAILED is a diagnostic warning, not a verdict.
if [ -s "$log" ] && grep -aE 'GYMCHAOS_[A-Z0-9_]*FAIL|error CS[0-9]+' "$log" | grep -qav 'GYMCHAOS_ROAM_ROUTE_FAILED'; then
  infra=false
fi
if [ "$infra" = true ]; then
  echo "::warning::Unity failed without a verifier or compiler error (license, crash or runner); retrying once."
  [ -s "$log" ] && tail -n 40 "$log"
fi
rm -f "$log"
echo "infra=$infra"
