#!/bin/bash
# TerminalHost devcontainer proxy — forwards Claude Code hooks to the host.
# Unlike the containerized workspace proxy, NO path translation is performed
# because code lives inside the container (not bind-mounted from host).
#
# Required env var:
#   TERMINALHOST_API  — host API URL (default: http://host.docker.internal:19280)
#
# Optional env var:
#   TERMINALHOST_DEVCONTAINER_NAME — display name for this container

API_URL="${TERMINALHOST_API:-http://host.docker.internal:19280}"
CONTAINER_NAME="${TERMINALHOST_DEVCONTAINER_NAME:-$(hostname)}"

if [ "$1" = "--hook" ] && [ -n "$2" ]; then
    PAYLOAD=$(cat)
    curl -s -X POST \
        -H "Content-Type: application/json" \
        -H "X-TerminalHost-Source: devcontainer" \
        -H "X-TerminalHost-Container: ${CONTAINER_NAME}" \
        -d "$PAYLOAD" \
        "$API_URL/api/hooks/$2" \
        > /dev/null 2>&1
    exit 0
fi

echo "TerminalHost devcontainer proxy"
echo "  API endpoint: $API_URL"
echo "  Container:    $CONTAINER_NAME"
