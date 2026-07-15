# HDR tone-mapping GStreamer plugin

`hdrtonemap` is a `GstBaseTransform` element. It accepts `P010_10LE` or `I420_10LE` PQ/HLG video and produces SDR `I420` with BT.709 colorimetry. With `use-opencl=true` it lazily probes an OpenCL GPU once, uses `tonemap_opencl` when available, and automatically falls back to the CPU graph if initialization or frame processing fails. `use-opencl=false` selects the CPU graph without probing OpenCL.

The OpenCL graph is:

```text
P010
hwupload
tonemap_opencl (Hable, BT.709 NV12)
hwdownload
scale (NV12 to I420 layout conversion)
```

The CPU fallback graph is:

```text
zscale (PQ/HLG to linear BT.2020)
format (gbrpf32le)
tonemap (Hable)
zscale (BT.2020 to BT.709 limited range)
format (yuv420p)
```

## Linux

The ready x64 plugin is stored at `runtimes/linux-x64/native/gstreamer-1.0/libgsthdrtonemap.so`. It requires glibc 2.35 or newer and GStreamer 1.20 or newer, and was tested on Ubuntu 22.04 and Debian 12. FFmpeg 8.0.3 and zimg 3.0.6 are linked statically, so users do not need FFmpeg/zimg development packages or a local build. The only additional runtime dependency beyond the documented GStreamer packages is the OpenCL loader:

```bash
apt-get install -y --no-install-recommends ocl-icd-libopencl1
```

The vendor OpenCL implementation comes from the installed GPU driver. If no OpenCL device is available, the same plugin automatically uses its embedded CPU graph.

To rebuild the plugin, install Meson, Ninja, a C/C++ compiler, Autotools, NASM/Yasm, GStreamer development packages, and OpenCL headers/loader development files. Provide unpacked zimg and FFmpeg source directories:

```bash
export ZIMG_SOURCE=/path/to/zimg-3.0.6
export FFMPEG_SOURCE=/path/to/ffmpeg-8.0.3
export DEPS_PREFIX=/tmp/gst-hdrtonemap-deps
export BUILD_DIR=/tmp/gst-hdrtonemap-build
./build-linux.sh
```

The script builds position-independent static zimg and the required CPU/OpenCL FFmpeg filters, links them into the plugin, rejects accidental dynamic FFmpeg/zimg dependencies, and copies the stripped result to `runtimes/linux-<arch>/native/gstreamer-1.0`.

## Windows

Use an MSYS2 MinGW64 shell matching the MinGW GStreamer distribution. Install the compiler toolchain, Meson, Ninja, pkg-config, make, autoconf, automake, libtool, nasm, OpenCL headers, and the OpenCL ICD loader:

```bash
pacman -S --needed mingw-w64-x86_64-opencl-headers mingw-w64-x86_64-opencl-icd
```

Provide unpacked zimg and FFmpeg source directories:

```bash
export ZIMG_SOURCE=/path/to/zimg
export FFMPEG_SOURCE=/path/to/ffmpeg
export DEPS_PREFIX=/c/gst-native-build/deps
export BUILD_DIR=/c/gst-native-build/plugin
export TMPDIR=/c/gst-native-build/tmp
export GSTREAMER_ROOT='/c/Program Files/gstreamer/1.0/mingw_x86_64'
./build-windows-msys2.sh
```

Keep the zimg, FFmpeg, dependency, build, and temporary paths free of spaces. The GStreamer path may contain spaces.

The script builds zimg and the required CPU/OpenCL FFmpeg filters as static libraries, links them into `libgsthdrtonemap.dll`, checks that no FFmpeg/zimg DLL remains, and copies the plugin plus the portable `OpenCL.dll` loader to `runtimes/win-x64/native/gstreamer-1.0`. The vendor OpenCL implementation still comes from the installed GPU driver; systems without one use the CPU fallback.

Do not replace the `libavfilter` DLL shipped with GStreamer. Review and satisfy the GStreamer, FFmpeg, and zimg license requirements before distributing native binaries.
