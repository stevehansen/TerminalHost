{
  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";

  outputs = { self, nixpkgs }:
    let
      supportedSystems = [ "x86_64-linux" "aarch64-linux" "x86_64-darwin" "aarch64-darwin" ];
      forEachSupportedSystem = f: nixpkgs.lib.genAttrs supportedSystems (system: f {
        pkgs = import nixpkgs { inherit system; };
      });
    in
    {
      devShells = forEachSupportedSystem ({ pkgs }:
        {
          default = pkgs.mkShell {
            packages = with pkgs; [
              dotnet-sdk_8
            ] ++ pkgs.lib.optionals pkgs.stdenv.isLinux [
              # Native deps for NuGet packages (LibGit2Sharp, SkiaSharp)
              zlib
              openssl
              fontconfig

              # Avalonia X11 backend
              libx11
              libxi
              libxcursor
              libxrandr
              libxext
              libice
              libsm
              libGL
            ];
            shellHook = ''
              export DOTNET_ROOT="${pkgs.dotnet-sdk_8}"
            '' + pkgs.lib.optionalString pkgs.stdenv.isLinux ''
              export LD_LIBRARY_PATH="${pkgs.lib.makeLibraryPath (with pkgs; [
                zlib openssl fontconfig
                libx11 libxi libxcursor libxrandr
                libxext libice libsm libGL
              ])}''${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
            '';
          };
        }
      );
    };
}
