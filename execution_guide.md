# 🚀 Nodus App - Execution Guide

Use these commands to run the applications from the `src` directory.

## 1. Nodus Server (Windows Admin Dashboard)
Runs the server application on your Windows machine.

```powershell
cd c:\code\project_kiosko\nodusApp\src
dotnet run --project Nodus.Server/Nodus.Server.csproj -f net10.0-windows10.0.19041.0
```

> **Note**: Verify that `Nodus.Server.exe` is not already running (zombie process) if the build fails.

## 2. Nodus Client (Android App)
Runs the client application on a connected Android device or emulator.

```powershell
cd c:\code\project_kiosko\nodusApp\src
dotnet run --project Nodus.Client/Nodus.Client.csproj -f net10.0-android
```

> **Requirements**:
> - Android Device connected via USB (debug mode enabled)
> - Or Android Emulator running
> - Verify connection with `adb devices`

## 🛠️ Common Troubleshooting

If the apps fail to start:

### Clean and Rebuild
Sometimes old artifacts cause issues.
```powershell
dotnet clean
dotnet build
```

### Kill Zombie Server Process
If the Server fails to build because "file is in use":
```powershell
taskkill /F /IM Nodus.Server.exe
```
