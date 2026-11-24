#!/usr/bin/env bash
# Build the TrellisKit website image (nginx with the website built for the neutral sample property).
# Builds for linux/amd64 and linux/arm64/v8 on Docker Build Cloud, pushes to Docker Hub, then pulls the pushed tags
# into the local image store. Tags: latest, plus the tag given as the first argument.
#
#   ./build-www.sh             # jchristn77/trelliskit-www:latest
#   ./build-www.sh v1.0.0      # jchristn77/trelliskit-www:v1.0.0 and jchristn77/trelliskit-www:latest
set -euo pipefail
cd "$(dirname "$0")"

IMAGE_NAME=jchristn77/trelliskit-www
DOCKERFILE_PATH=docker/www/Dockerfile
PLATFORMS=linux/amd64,linux/arm64/v8
BUILDER=cloud-jchristn77-jchristn77
CLOUD_ENDPOINT=jchristn77/jchristn77
TAG="${1:-latest}"

TAGS=(--tag "$IMAGE_NAME:latest")
if [ "$TAG" != "latest" ]; then TAGS+=(--tag "$IMAGE_NAME:$TAG"); fi

echo "Image:     $IMAGE_NAME:$TAG"
echo "Platforms: $PLATFORMS"
docker buildx use "$BUILDER" 2>/dev/null || docker buildx create --driver cloud "$CLOUD_ENDPOINT" --use
docker buildx build \
  --builder "$BUILDER" \
  --platform "$PLATFORMS" \
  --file "$DOCKERFILE_PATH"  \
  "${TAGS[@]}" \
  --push \
  .

echo "Pulling the pushed tags into the local image store..."
docker pull "$IMAGE_NAME:latest"
if [ "$TAG" != "latest" ]; then docker pull "$IMAGE_NAME:$TAG"; fi
echo "Done: $IMAGE_NAME:$TAG"
