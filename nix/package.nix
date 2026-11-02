{
  lib,
  buildDotnetModule,
  dotnetCorePackages,
  coreutils,
  glfw,
  glslang,
  libGL,
  libxext,
  libxi,
  libxrandr,
  llvmPackages,
  openal,
  python3,
  vulkan-loader,
}:

let
  # Listed per directory because game/ also tracks player data in Replay/ and backups/.
  assets = lib.fileset.toSource {
    root = ../game;
    fileset = lib.fileset.unions [
      ../game/BGM
      ../game/CS
      ../game/FX
      ../game/Image
      ../game/Model
      ../game/Sound
      ../game/Story
      ../game/Setting.INI
    ];
  };
in
buildDotnetModule (finalAttrs: {
  pname = "thsss";
  version = "0-unstable-2026-10-30";

  __structuredAttrs = true;
  strictDeps = true;

  src = lib.fileset.toSource {
    root = ../.;
    fileset = lib.fileset.unions [
      ../global.json
      ../TargetFrameworks.props
      ../src
      # Referenced by src/THSSS.csproj.
      ../game/Setting.INI
      ../tools/GameplayMath.Fody
      ../native/LegacyMath
    ];
  };

  # Unwrapped, because the cc-wrapper adds glibc paths and frame-pointer flags
  # to the freestanding math kernel that native/LegacyMath/build.py compiles.
  nativeBuildInputs = [
    llvmPackages.clang-unwrapped
    llvmPackages.lld
    python3
  ];

  dotnet-sdk = dotnetCorePackages.sdk_8_0;
  dotnet-runtime = dotnetCorePackages.runtime_8_0;

  projectFile = [
    "src/Config/THSSS.Config.csproj"
    "src/THSSS.csproj"
  ];
  nugetDeps = ./deps.json;
  # dotnet publish rejects multi-targeted projects, which TargetFrameworks.props makes both.
  dotnetInstallFlags = [
    "--framework"
    "net8.0"
  ];

  runtimeDeps = [
    glfw
    libGL
    openal
    vulkan-loader
    # For Avalonia. Its NuGet setup hook already adds libx11, libice, libsm and libxcursor.
    libxext
    libxi
    libxrandr
  ];

  makeWrapperArgs = [
    # The game writes saves, replays, screenshots and Setting.INI into its data
    # root, so the root is a per-user directory with links to the store assets.
    "--run"
    ''
      THSSS_DATA_ROOT=''${THSSS_DATA_ROOT:-''${XDG_DATA_HOME:-$HOME/.local/share}/thsss}
      ${coreutils}/bin/mkdir -p "$THSSS_DATA_ROOT"
      for dir in BGM CS FX Image Model Sound Story; do
        if [ -L "$THSSS_DATA_ROOT/$dir" ] || [ ! -e "$THSSS_DATA_ROOT/$dir" ]; then
          ${coreutils}/bin/ln -sfn ${assets}/$dir "$THSSS_DATA_ROOT/$dir"
        fi
      done
      if [ ! -e "$THSSS_DATA_ROOT/Setting.INI" ]; then
        ${coreutils}/bin/install -m 644 ${assets}/Setting.INI "$THSSS_DATA_ROOT/Setting.INI"
      fi
      export THSSS_DATA_ROOT
    ''
    # The Vulkan renderer (THSSS_RENDERER=vk) compiles shaders at runtime.
    "--suffix"
    "PATH"
    ":"
    (lib.makeBinPath [ glslang ])
  ];

  postInstall = ''
    # The NuGet GLFW dlopens X11 and Wayland by bare soname, which fails on NixOS.
    # The wrapper copies Setting.INI into the per-user data root.
    rm $out/lib/thsss/libglfw.so.3 $out/lib/thsss/Setting.INI
  '';

  # The default fixup wraps every executable in lib/thsss, including the
  # native .so files, and keeps the generic names THSSS and Config.
  dontDotnetFixup = true;
  preFixup = ''
    wrapDotnetProgram $out/lib/thsss/THSSS $out/bin/thsss
    wrapDotnetProgram $out/lib/thsss/Config $out/bin/thsss-config
  '';

  passthru = { inherit assets; };

  meta = {
    description = "Cross-platform .NET port of Touhou Hisou Sousou (东方夏夜祭)";
    homepage = "https://github.com/northgreen/thsss-crossplartform";
    mainProgram = "thsss";
    # macOS also needs the Metal shader cache that tools/make-dist.pl generates.
    platforms = lib.platforms.linux;
  };
})
