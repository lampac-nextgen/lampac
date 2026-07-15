#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
gstreamer_root="${GSTREAMER_ROOT:-/c/Program Files/gstreamer/1.0/mingw_x86_64}"
zimg_source="${ZIMG_SOURCE:?Set ZIMG_SOURCE to an unpacked zimg source directory}"
ffmpeg_source="${FFMPEG_SOURCE:?Set FFMPEG_SOURCE to an unpacked FFmpeg source directory}"
dependency_prefix="${DEPS_PREFIX:-$script_dir/build-windows-deps}"
build_dir="${BUILD_DIR:-$script_dir/build-windows-x64}"
output_dir="$script_dir/runtimes/win-x64/native/gstreamer-1.0"
jobs="${JOBS:-$(nproc)}"
ffmpeg_marker="$dependency_prefix/.ffmpeg-hdrtonemap-opencl-v1"
opencl_dll="${OPENCL_DLL:-/mingw64/bin/OpenCL.dll}"

if [[ -n "${TMPDIR:-}" ]]; then
  export TMP="$TMPDIR"
  export TEMP="$TMPDIR"
fi

if [[ ! -f "$dependency_prefix/lib/pkgconfig/zimg.pc" ]]; then
  pushd "$zimg_source"
  if [[ ! -x ./configure ]]; then ./autogen.sh; fi
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
    --disable-shared --enable-static --enable-pic \
    --disable-programs --disable-doc --disable-debug --disable-network \
    --disable-everything --enable-avfilter --enable-swscale --enable-libzimg \
    --enable-opencl \
    --enable-filter=buffer,buffersink,format,hwdownload,hwupload,scale,tonemap,tonemap_opencl,zscale
  make -j"$jobs"
  make install
  touch "$ffmpeg_marker"
  popd
fi

export PKG_CONFIG_PATH="$dependency_prefix/lib/pkgconfig:$dependency_prefix/lib64/pkgconfig:$gstreamer_root/lib/pkgconfig"
if [[ -f "$build_dir/build.ninja" ]]; then
  meson setup --reconfigure --clearcache "$build_dir" "$script_dir" -Dstatic-ffmpeg=true --buildtype=release
else
  meson setup "$build_dir" "$script_dir" -Dstatic-ffmpeg=true --buildtype=release
fi
meson compile -C "$build_dir"
mkdir -p "$output_dir"
cp "$build_dir/libgsthdrtonemap.dll" "$output_dir/"
if [[ ! -f "$opencl_dll" ]]; then
  echo "OpenCL loader not found: $opencl_dll" >&2
  exit 1
fi
cp "$opencl_dll" "$output_dir/OpenCL.dll"

if ldd "$output_dir/libgsthdrtonemap.dll" | grep -Eiq 'avfilter|avutil|swscale|zimg'; then
  echo "FFmpeg or zimg remained dynamically linked" >&2
  exit 1
fi
export PATH="$output_dir:$gstreamer_root/bin:$PATH"
export GST_PLUGIN_PATH="$output_dir${GST_PLUGIN_PATH:+:$GST_PLUGIN_PATH}"
gst-inspect-1.0 hdrtonemap
echo "Plugin: $output_dir/libgsthdrtonemap.dll"
