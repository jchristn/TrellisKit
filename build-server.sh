#!/usr/bin/env bash
# Build the TrellisKit server image: the Dockerfile's combined target (the server with ffmpeg plus the website on
# port 8080 in one container), built for and baking in the neutral sample property.
# Builds for linux/amd64 and linux/arm64/v8 on Docker Build Cloud, pushes to Docker Hub, then pulls the pushed tags
# into the local image store. Tags: latest, plus the tag given as the first argument.
#
#   ./build-server.sh             # jchristn77/trelliskit-server:latest
#   ./build-server.sh v1.0.0      # jchristn77/trelliskit-server:v1.0.0 and jchristn77/trelliskit-server:latest
set -euo pipefail
cd "$(dirname "$0")"

IMAGE_NAME=jchristn77/trelliskit-server
DOCKERFILE_PATH=docker/server/Dockerfile
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
  --file "$DOCKERFILE_PATH" --target combined \
  "${TAGS[@]}" \
  --push \
  .

echo "Pulling the pushed tags into the local image store..."
docker pull "$IMAGE_NAME:latest"
if [ "$TAG" != "latest" ]; then docker pull "$IMAGE_NAME:$TAG"; fi
echo "Done: $IMAGE_NAME:$TAG"
