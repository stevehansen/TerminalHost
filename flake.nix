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
      packages = forEachSupportedSystem ({ pkgs }:
        let
          dotnet = with pkgs.dotnetCorePackages; combinePackages [ sdk_8_0 sdk_10_0 ];
          linuxDeps = import ./nix/linux-deps.nix pkgs;
        in
        {
          default = pkgs.buildDotnetModule {
            pname = "TerminalHost";
            version = "0.0.0";
            src = self;
            projectFile = "src/TerminalHost.Avalonia/TerminalHost.Avalonia.csproj";
            nugetDeps = ./deps.json;
            dotnet-sdk = dotnet;
            dotnet-runtime = pkgs.dotnetCorePackages.runtime_8_0;
            selfContainedBuild = true;
            runtimeDeps = pkgs.lib.optionals pkgs.stdenv.isLinux linuxDeps;
            # Fix URL-encoded '+' (%2B) in NuGet PCL framework folder names.
            # unzip preserves literal %2B from nupkg zips, but dotnet expects real '+'.
            # Must run before restore so project.assets.json gets correct paths.
            postConfigureNuGet = ''
              find "''${NUGET_FALLBACK_PACKAGES:-.}" -maxdepth 2 -type l | while read -r link; do
                target=$(readlink "$link")
                if find "$target" -name '*%2B*' -print -quit 2>/dev/null | grep -q .; then
                  rm "$link"
                  cp -rL "$target" "$link"
                  chmod -R u+w "$link"
                  find "$link" -depth -name '*%2B*' | while read -r f; do
                    mv "$f" "$(echo "$f" | sed 's/%2B/+/g')"
                  done
                fi
              done
            '';
            meta.mainProgram = "host";
          };
        }
      );

      devShells = forEachSupportedSystem ({ pkgs }:
        let
          dotnet = with pkgs.dotnetCorePackages; combinePackages [ sdk_8_0 sdk_10_0 ];
          linuxDeps = import ./nix/linux-deps.nix pkgs;
        in
        {
          default = pkgs.mkShell {
            packages = [ dotnet pkgs.nodejs_22 ]
              ++ pkgs.lib.optionals pkgs.stdenv.isLinux linuxDeps;
            shellHook = ''
              export DOTNET_ROOT="${dotnet}"
            '' + pkgs.lib.optionalString pkgs.stdenv.isLinux ''
              export LD_LIBRARY_PATH="${pkgs.lib.makeLibraryPath linuxDeps}''${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
            '';
          };
        }
      );
    };
}
