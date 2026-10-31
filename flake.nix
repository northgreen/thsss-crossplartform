{
  outputs =
    {
      flake-utils,
      nixpkgs,
      self,
    }:
    let
      flakeOutputs = flake-utils.lib.eachDefaultSystem (
        system:
        let
          pkgs = import nixpkgs {
            localSystem = {
              inherit system;
            };
            overlays = [ self.overlays.default ];
          };
          inherit (pkgs) lib;
        in
        {
          legacyPackages = pkgs;
          packages = (lib.attrsets.intersectAttrs (self.overlays.default pkgs pkgs) pkgs) // {
            default = pkgs.thsss;
          };
        }
      );
    in
    flakeOutputs
    // {
      overlays.default = import ./nix/overlay.nix;
    };
  inputs = {
    nixpkgs.url = "https://channels.nixos.org/nixpkgs-unstable/nixexprs.tar.zst";
    flake-utils.url = "github:numtide/flake-utils";
  };
}
