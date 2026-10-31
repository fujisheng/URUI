# Migrating the previous project to the UPM layout

The previous project snapshot is preserved in Git history at a29a15ebf55b1b26fcfb47bf73fc6105b1d0b563; use a separate checkout of that commit for the old samples and measurements. The local project/research snapshots are also retained. Current main is a package distribution, not a Unity project template: it intentionally has no active Assets/RmlUnity, root Native or root ProjectSettings.

1. Back up or commit your application and its scenes. Record custom changes to the old framework copy.
2. Remove the previous Assets/RmlUnity framework scripts/assembly definitions and old plugin importers from the application before installing this package. Do not keep both active. Move any application-owned game/sample content separately and resolve old scene references as part of migration.
3. Copy Packages/com.fujisheng.rmlunity into your application's Packages directory, or add its package.json from disk. Existing framework script/shader GUIDs and assembly names are preserved where files moved; the package retains managed Host APIs. The standard renderer remains default, demo click mutations default off, and experimental paths require explicit opt-in.
4. Use the optional Hello Rml sample or Tools > RmlUnity > Create Minimal UI Scene. Supply a MainCamera, document and font as documented. This bridge supports one live native context. Keep the legacy Input backend enabled in the application's input settings.

The package's Native~ includes full native source and vendored versions needed for an offline Mac ARM64 rebuild. Directories ending in ~ are deliberately ignored by Unity asset/C# import; inspect them in a file manager or code editor. They are not missing files.

Legacy Android workflows were not edited or run. They refer to the old project layout and should not be treated as current validated package pipelines. This commit intentionally skips CI; local functional validation is documented separately. No new platform support, performance improvement, or full legacy fault/performance-matrix rerun is claimed.
