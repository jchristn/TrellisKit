# TrellisKit website (marketing, member app, admin console), built for one property.
# Build context: the engine root, so the shared workspace package is available. The property is a separate named
# build context called "property"; without one, the neutral sample property (samples/example) is used.
#   docker build -f docker/www/Dockerfile --build-context property=<property folder> -t trelliskit/www:<property id> .

# ---- Property (overridden by --build-context property=<folder> or compose additional_contexts) ----
FROM scratch AS property
COPY samples/example/ /

# ---- Build stage ----
FROM node:22-alpine AS build
WORKDIR /repo

# Install dependencies first for better layer caching.
COPY package.json package-lock.json ./
COPY shared/package.json shared/package.json
COPY www/package.json www/package.json
RUN npm ci --no-audit --no-fund

# Copy sources and the property, then build the SPA for that property.
COPY shared/ shared/
COPY www/ www/
COPY --from=property / /property/
ENV TK_PROPERTY_DIR=/property
RUN npm run build --workspace www

# ---- Runtime stage ----
FROM nginx:1.27-alpine AS runtime

# curl is required for container health checks.
RUN apk add --no-cache curl

# nginx.conf is a template: the image entrypoint substitutes TK_EMBED_FRAME_ANCESTORS into conf.d/default.conf at
# start (compose passes it; the default allows only same-origin framing of the assistant embed page).
ENV TK_EMBED_FRAME_ANCESTORS="'self'" \
    TK_WWW_BACKEND=http://server:8000
COPY docker/www/nginx.conf /etc/nginx/templates/default.conf.template
RUN rm -f /etc/nginx/conf.d/default.conf
COPY --from=build /repo/www/dist /usr/share/nginx/html

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -fsS http://127.0.0.1:8080/healthz || exit 1

CMD ["nginx", "-g", "daemon off;"]
