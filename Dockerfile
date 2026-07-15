# Multi-platform Dockerfile for linux/amd64 and linux/arm64
# Build with: docker buildx build --platform linux/amd64,linux/arm64 -f Dockerfile .

# Global ARGs
ARG DOTNET_VERSION=10.0.10
ARG DOTNET_SDK_VERSION=10.0.302
ARG CHROMIUM_VERSION=150.0.7871.100-1~deb13u1

# Builder image — platform set by buildx
FROM --platform=$BUILDPLATFORM debian:13-slim AS builder

ARG BUILDARCH
ARG TARGETARCH
ARG DOTNET_VERSION
ARG DOTNET_SDK_VERSION

RUN mkdir -p /out

WORKDIR /build

COPY . .

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
    ca-certificates \
    curl \
    libicu76 \
    xz-utils \
    && rm -rf /var/lib/apt/lists/*

RUN case "$BUILDARCH" in \
    arm64) \
    DOTNET_SDK_URL="https://builds.dotnet.microsoft.com/dotnet/Sdk/${DOTNET_SDK_VERSION}/dotnet-sdk-${DOTNET_SDK_VERSION}-linux-arm64.tar.gz" \
    ;; \
    amd64) \
    DOTNET_SDK_URL="https://builds.dotnet.microsoft.com/dotnet/Sdk/${DOTNET_SDK_VERSION}/dotnet-sdk-${DOTNET_SDK_VERSION}-linux-x64.tar.gz" \
    ;; \
    *) echo "Unsupported BUILDARCH: $BUILDARCH" && exit 1 ;; \
    esac \
    && case "$TARGETARCH" in \
    arm64) \
    DOTNET_RUNTIME_URL="https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/${DOTNET_VERSION}/aspnetcore-runtime-${DOTNET_VERSION}-linux-arm64.tar.gz" \
    FFMPEG_URL="https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linuxarm64-gpl.tar.xz" \
    RID=linux-arm64 \
    ;; \
    amd64) \
    DOTNET_RUNTIME_URL="https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/${DOTNET_VERSION}/aspnetcore-runtime-${DOTNET_VERSION}-linux-x64.tar.gz" \
    FFMPEG_URL="https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linux64-gpl.tar.xz" \
    RID=linux-x64 \
    ;; \
    *) echo "Unsupported TARGETARCH: $TARGETARCH" && exit 1 ;; \
    esac \
    # SDK — required for dotnet publish
    && curl -fSL -o /tmp/dotnet-sdk.tar.gz "${DOTNET_SDK_URL}" \
    && mkdir -p /out/usr/share/dotnet \
    && tar -oxzf /tmp/dotnet-sdk.tar.gz -C /out/usr/share/dotnet \
    && rm /tmp/dotnet-sdk.tar.gz \
    # Build the application
    && DOTNET_CLI_TELEMETRY_OPTOUT=1 /out/usr/share/dotnet/dotnet publish --configuration Release --runtime "$RID" --output /out/lampac -p:PlaywrightPlatform="$RID" Core/Core.csproj \
    # Replace SDK with ASP.NET Core runtime for the final image
    && rm -rf /out/usr/share/dotnet \
    && mkdir -p /out/usr/share/dotnet \
    && curl -fSL -o /tmp/dotnet-runtime.tar.gz "${DOTNET_RUNTIME_URL}" \
    && tar -oxzf /tmp/dotnet-runtime.tar.gz -C /out/usr/share/dotnet \
    && rm /tmp/dotnet-runtime.tar.gz \
    # FFmpeg & FFprobe — binaries only
    && curl -fSL -o /tmp/ffmpeg.tar.xz "${FFMPEG_URL}" \
    && tar -xJf /tmp/ffmpeg.tar.xz -C /tmp \
    --wildcards "*/bin/ffmpeg" "*/bin/ffprobe" \
    --strip-components=2 \
    && mv /tmp/ffmpeg /tmp/ffprobe /out/lampac/data/ \
    && chmod +x /out/lampac/data/ffmpeg /out/lampac/data/ffprobe \
    && rm /tmp/ffmpeg.tar.xz \
    && touch /out/lampac/isdocker

# Runner — OS/arch of the published image (amd64 vs arm64)
FROM debian:13-slim AS runner

ARG TARGETARCH
ARG CHROMIUM_VERSION

LABEL org.opencontainers.image.description="Lampac NextGen - Media aggregator" \
    org.opencontainers.image.licenses="MIT" \
    org.opencontainers.image.source="https://github.com/lampac-nextgen/lampac" \
    org.opencontainers.image.vendor="Lampac NextGen"

ENV DOTNET_ROOT=/usr/share/dotnet \
    PATH="${PATH}:/usr/share/dotnet" \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    CHROMIUM_PATH=/usr/bin/chromium \
    CHROMIUM_FLAGS="--no-sandbox --disable-setuid-sandbox --disable-dev-shm-usage"

WORKDIR /lampac
EXPOSE 9118

# Runtime dependencies
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && mkdir -p /tmp/chromium && cd /tmp/chromium \
    && BASE="https://snapshot.debian.org/archive/debian/20260710T142757Z/pool/main/c/chromium" \
    && case "$TARGETARCH" in \
    amd64) \
    SHA_CHROMIUM=87ce517f9fe47c4dcac35fc314fa4ab87117f2496dc27257de2bba11ef8af610 \
    SHA_COMMON=f5e636f3535e7fc5c688e2b01554e87755eb4b84fca071d9e47e7670d35d0564 \
    SHA_SANDBOX=a02bc28af35c9cdbaaafb0affa004fa203cf4508d4c7fa280efdc7c521a380c3 \
    ;; \
    arm64) \
    SHA_CHROMIUM=28cfcb13137ff92affba7495cfe8ddc08b33c008b0fd12f1dc357a2fdbc139a3 \
    SHA_COMMON=2fcd3948e09dc08c939eeb1bc32e5e99afeb761fe98135d41cfb39675de95810 \
    SHA_SANDBOX=91f2e4b7de964f2635f4c83a02125303a2932b3b4e0cdfb64c6ba8ec7cfd1b24 \
    ;; \
    *) echo "Unsupported TARGETARCH: $TARGETARCH" && exit 1 ;; \
    esac \
    && for pkg in chromium chromium-common chromium-sandbox; do \
    curl -fSL -o "${pkg}.deb" "${BASE}/${pkg}_${CHROMIUM_VERSION}_${TARGETARCH}.deb"; \
    done \
    && printf '%s  %s\n' \
    "$SHA_CHROMIUM" chromium.deb \
    "$SHA_COMMON" chromium-common.deb \
    "$SHA_SANDBOX" chromium-sandbox.deb \
    | sha256sum -c - \
    && apt-get install -y --no-install-recommends ./*.deb \
    && rm -rf /tmp/chromium \
    && apt-get install -y --no-install-recommends \
    curl \
    fontconfig \
    gstreamer1.0-libav \
    gstreamer1.0-plugins-bad \
    gstreamer1.0-plugins-base \
    gstreamer1.0-plugins-base-apps \
    gstreamer1.0-plugins-good \
    gstreamer1.0-plugins-ugly \
    gstreamer1.0-tools \
    imagemagick \
    libgstreamer-plugins-base1.0-0 \
    libgstreamer1.0-0 \
    libicu76 \
    libjpeg-dev \
    libnspr4 \
    libpng-dev \
    libwebp-dev \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/* \
    && rm -rf \
    /usr/share/doc \
    /usr/share/man \
    /usr/share/info \
    /usr/share/common-licenses

# Create non-root user before COPY to use --chown
RUN groupadd -r -g 1000 lampac \
    && useradd -r -u 1000 -g lampac -d /lampac lampac

# Copy application
COPY --chown=lampac:lampac --from=builder /out /

# Health check — verify process is running
HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
    CMD pgrep -x dotnet || exit 1

USER lampac

ENTRYPOINT ["/usr/share/dotnet/dotnet", "Core.dll"]
