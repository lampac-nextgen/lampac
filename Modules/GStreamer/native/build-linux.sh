#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
architecture="$(uname -m)"
case "$architecture" in
  x86_64) runtime_id="linux-x64" ;;
  aarch64|arm64) runtime_id="linux-arm64" ;;
  *) echo "Unsupported architecture: $architecture" >&2; exit 1 ;;
esac

zimg_source="${ZIMG_SOURCE:?Set ZIMG_SOURCE to an unpacked zimg source directory}"
ffmpeg_source="${FFMPEG_SOURCE:?Set FFMPEG_SOURCE to an unpacked FFmpeg source directory}"
dependency_prefix="${DEPS_PREFIX:-$script_dir/build-linux-deps-$architecture}"
build_dir="${BUILD_DIR:-$script_dir/build-linux-$architecture}"
output_dir="$script_dir/runtimes/$runtime_id/native/gstreamer-1.0"
jobs="${JOBS:-$(nproc)}"
ffmpeg_marker="$dependency_prefix/.ffmpeg-hdrtonemap-opencl-v1"

if [[ ! -f "$dependency_prefix/lib/pkgconfig/zimg.pc" ]]; then
  pushd "$zimg_source"
  if [[ ! -x ./configure ]]; then ./autogen.sh; fi
  CFLAGS="${CFLAGS:+$CFLAGS }-fPIC" \
    CXXFLAGS="${CXXFLAGS:+$CXXFLAGS }-fPIC" \
    ./configure --prefix="$dependency_prefix" --disable-shared --enable-static
  make -j"$jobs"
  make install
  popd
fi

export PKG_CONFIG_PATH="$dependency_prefix/lib/pkgconfig:$dependency_prefix/lib64/pkgconfig"
if [[ ! -f "$dependency_prefix/lib/pkgconfig/libavfilter.pc" || ! -f "$ffmpeg_marker" ]]; then
  pushd "$ffmpeg_source"
  if [[ -f ffbuild/config.mak ]]; then make distclean; fi
  ./configure \
    --prefix="$dependency_prefix" \
    --pkg-config-flags=--static \
    --extra-cflags="-I$dependency_prefix/include" \
    --extra-ldflags="-L$dependency_prefix/lib" \
    --extra-libs=-lm \
    --disable-shared --enable-static --enable-pic \
    --disable-programs --disable-doc --disable-debug --disable-network \
    --disable-autodetect --disable-everything \
    --enable-avfilter --enable-swscale --enable-libzimg --enable-opencl \
    --enable-filter=buffer,buffersink,format,hwdownload,hwupload,scale,tonemap,tonemap_opencl,zscale
  make -j"$jobs"
  make install
  touch "$ffmpeg_marker"
  popd
fi

export PKG_CONFIG_PATH="$dependency_prefix/lib/pkgconfig:$dependency_prefix/lib64/pkgconfig"
if [[ -f "$build_dir/build.ninja" ]]; then
  meson setup --reconfigure --clearcache "$build_dir" "$script_dir" -Dstatic-ffmpeg=true --buildtype=release
else
  meson setup "$build_dir" "$script_dir" -Dstatic-ffmpeg=true --buildtype=release
fi
meson compile -C "$build_dir"
mkdir -p "$output_dir"
cp "$build_dir/libgsthdrtonemap.so" "$output_dir/"
strip --strip-unneeded "$output_dir/libgsthdrtonemap.so"

if ldd "$output_dir/libgsthdrtonemap.so" | grep -Eiq 'avfilter|avutil|swscale|zimg'; then
  echo "FFmpeg or zimg remained dynamically linked" >&2
  exit 1
fi
if ldd "$output_dir/libgsthdrtonemap.so" | grep -q 'not found'; then
  ldd "$output_dir/libgsthdrtonemap.so" >&2
  exit 1
fi
GST_PLUGIN_PATH="$output_dir${GST_PLUGIN_PATH:+:$GST_PLUGIN_PATH}" gst-inspect-1.0 hdrtonemap
echo "Plugin: $output_dir/libgsthdrtonemap.so"
