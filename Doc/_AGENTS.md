# Deployment (AppX hot-reload) 

Windows must be in Developer Mode.

1. Unpack "compiled" appx (look for AppPackages folder):
```
makeappx unpack /p Package.appx /d unpacked\
REM or just unzip
del unpacked\AppxSignature.p7x
powershell add-appxpackage -register unpacked\AppxManifest.xml
```

If msbuild produced an **appxbundle** instead of appx, double-unpack:
1. Extract `.appxbundle` → get `.appx` (pick x64)
2. Extract `.appx` → get package

Then register the unpacked folder as above.

---

## File logging (DevTools)

The `DevToolsLogger` writes every log entry (including `Console.log` from the JavaScript engine and test‑framework messages) to
`{LocalFolder}\Logger.txt` (i.e. `%LOCALAPPDATA%\Packages\MediaExplorerV0p55_*\LocalState\Logger.txt`).

The file is created on first log write.
You can view the live log while the app runs, or open it after the app exits.

### PowerShell helper

A helper script `DeployAndRun.ps1` is provided (see next section). It:

1. Builds the project (`msbuild`).
2. Unpacks the generated **.appx** (or **.appxbundle**) into a temporary folder.
3. Registers the unpacked package (`Add‑AppxPackage -Register …`).
4. Starts the app (`Start‑Process …`).
5. Waits for termination (`Stop‑Process` is used to close the app when you press **Enter**).
6. Dumps the content of `Logger.txt` to the console.

Use it during iterative development: fix parsing/rendering bugs, rebuild, and repeat without manual steps.
