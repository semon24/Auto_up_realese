#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

TAG="${1:-latest}"
REGISTRY="${REGISTRY:-registry.ft-soft.ru/devops}"

AGENT_IMAGE="${REGISTRY}/agent-auto-up-release:${TAG}"

echo "Building AGENT image: ${AGENT_IMAGE}"
docker build -f agent/Dockerfile -t "${AGENT_IMAGE}" agent

echo "Pushing AGENT image: ${AGENT_IMAGE}"
docker push "${AGENT_IMAGE}"

echo "Done."
