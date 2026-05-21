#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

TAG="${1:-latest}"
REGISTRY="${REGISTRY:-registry.ft-soft.ru/devops}"

API_IMAGE="${REGISTRY}/api-auto-up-release:${TAG}"
WEB_IMAGE="${REGISTRY}/web-auto-up-release:${TAG}"

echo "Building API image: ${API_IMAGE}"
docker build -f Api/Dockerfile -t "${API_IMAGE}" Api

echo "Building WEB image: ${WEB_IMAGE}"
docker build -f client/Dockerfile -t "${WEB_IMAGE}" client

echo "Pushing API image: ${API_IMAGE}"
docker push "${API_IMAGE}"

echo "Pushing WEB image: ${WEB_IMAGE}"
docker push "${WEB_IMAGE}"

echo "Done."
