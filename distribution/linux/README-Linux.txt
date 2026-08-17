Scrap Mechanic Mod Manager — Linux preview
===========================================

This is a self-contained x64 Avalonia launcher for Steam Proton installations of
Scrap Mechanic. It does not require a separate .NET installation.

Required desktop libraries on Debian/Ubuntu:

  sudo apt install libx11-6 libice6 libsm6 libfontconfig1

Portable launch:

  1. Extract the complete archive.
  2. Run: chmod +x ScrapMechanicModManager scrap-mechanic-mod-manager
  3. Run: ./scrap-mechanic-mod-manager

The launcher checks these default Steam roots:

  ~/.local/share/Steam
  ~/.steam/steam
  ~/.var/app/com.valvesoftware.Steam/.local/share/Steam

Other Steam libraries listed in steamapps/libraryfolders.vdf are discovered
automatically. A game root can also be selected manually.

Safety rules:

  - Do not run the launcher with sudo.
  - Installation stops if the Steam library is not writable.
  - The launcher rejects unknown Scrap Mechanic builds and a running game.
  - Every modified game file is backed up before replacement.
  - Cache handling only targets Cache/Bundle/core_data.cbo.

The included .desktop file expects scrap-mechanic-mod-manager to be available in
PATH. Desktop integration is optional; the portable launcher works directly from
the extracted directory.

Linux support remains a preview until installation, launch, gameplay, and restore
have been verified on a real Steam Proton system.
