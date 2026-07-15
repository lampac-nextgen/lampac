project('gst-hdr-tone-map', 'c', version: '0.2.0',
  default_options: ['c_std=c11', 'warning_level=2'])

cc = meson.get_compiler('c')
gst_dep = dependency('gstreamer-1.0', version: '>=1.20')
gst_base_dep = dependency('gstreamer-base-1.0', version: '>=1.20')
gst_video_dep = dependency('gstreamer-video-1.0', version: '>=1.20')

static_ffmpeg = get_option('static-ffmpeg')
avfilter_dep = dependency('libavfilter', static: static_ffmpeg)
avutil_dep = dependency('libavutil', static: static_ffmpeg)
swscale_dep = dependency('libswscale', static: static_ffmpeg)
zimg_dep = dependency('zimg', static: static_ffmpeg)

plugin_deps = [avfilter_dep, avutil_dep, swscale_dep, zimg_dep,
  gst_dep, gst_base_dep, gst_video_dep]
if static_ffmpeg
  plugin_deps += cc.find_library('OpenCL', required: true)
endif

link_args = []
if static_ffmpeg and cc.get_id() in ['gcc', 'clang']
  link_args += ['-Wl,--exclude-libs,ALL']
  if host_machine.system() == 'windows'
    link_args += [
      '-static-libgcc',
      '-Wl,-Bstatic',
      '-lwinpthread',
      '-Wl,-Bdynamic',
    ]
  endif
endif

shared_module(
  'gsthdrtonemap',
  'src/gsthdrtonemap.c',
  name_prefix: 'lib',
  dependencies: plugin_deps,
  link_args: link_args,
  gnu_symbol_visibility: 'hidden',
  install: true,
  install_dir: join_paths(get_option('libdir'), 'gstreamer-1.0')
)
