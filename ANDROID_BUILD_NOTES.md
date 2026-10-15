# Android build and launch checks

Use a complete MAUI build before deploying. Do not use `dotnet build -t:Compile` as a deployable build: it can overwrite the application assembly without completing the XAML/resource pipeline.

```powershell
dotnet build GeoSilence.csproj -f net9.0-android --no-restore
```

If a debug deployment reports `XamlParseException: No embeddedresource found for GeoSilence.App`, rebuild the full Android target and redeploy:

```powershell
dotnet build GeoSilence.csproj -f net9.0-android --no-restore -t:Rebuild
```

For a self-contained debug APK (rather than fast-deployed assemblies), add `-p:EmbedAssembliesIntoApk=true`. Check that installation succeeded; low emulator storage can leave an older package installed. Do not uninstall or clear app data merely to refresh build artifacts.

## Investigation on 2026-10-13

The emulator's fast-deployed `GeoSilence.dll` lacked `GeoSilence.App.xaml`, while `App.InitializeComponent` still called the runtime XAML loader. Crashes occurred in `App` construction before the map or Explore page could load. A full rebuild restored the resource. The complete assembly and matching symbols were copied to the existing debug deployment and verified by SHA-256, preserving app data. The earlier compile-only password-reset validation was insufficient and is consistent with this failure.

Diagnostic artifacts are under `obj/`: `launch-before.dll`, `launch-repair-build.log`, and `launch-repair-screen.png`.
