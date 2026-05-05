#!/usr/bin/env bash
# Declares the `player_id` attribute on the players realm's user profile.
# Keycloak v25+ silently drops attributes not declared in the user profile schema.
# Run once after `docker compose up` (idempotent).
#
# Usage: ./infra/keycloak/setup/declare-player-id.sh

set -euo pipefail

KC_URL="${KC_URL:-http://localhost:8080}"
ADMIN_USER="${KC_ADMIN_USER:-admin}"
ADMIN_PASS="${KC_ADMIN_PASS:-admin}"
REALM="${KC_PLAYERS_REALM:-players}"

echo "Fetching admin token..."
TOKEN=$(curl -fsS -X POST "${KC_URL}/realms/master/protocol/openid-connect/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password&client_id=admin-cli&username=${ADMIN_USER}&password=${ADMIN_PASS}" \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['access_token'])")

echo "Reading current ${REALM} user profile..."
PROFILE=$(curl -fsS -H "Authorization: Bearer ${TOKEN}" \
  "${KC_URL}/admin/realms/${REALM}/users/profile")

echo "Adding player_id attribute (idempotent)..."
UPDATED=$(echo "$PROFILE" | python3 -c "
import sys, json
p = json.load(sys.stdin)
if not any(a['name'] == 'player_id' for a in p['attributes']):
    p['attributes'].append({
        'name': 'player_id',
        'displayName': 'PAM Player Id',
        'permissions': {'view': ['admin'], 'edit': ['admin']},
        'multivalued': False
    })
print(json.dumps(p))
")

curl -fsS -o /dev/null -X PUT "${KC_URL}/admin/realms/${REALM}/users/profile" \
  -H "Authorization: Bearer ${TOKEN}" \
  -H "Content-Type: application/json" \
  -d "$UPDATED"

echo "Done. ${REALM} user profile now declares player_id."
