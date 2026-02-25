# 🚀 Nodus App - Execution Guide

Use these commands to run the Nodus ecosystem applications. It is recommended to run each command in a separate terminal window from the `src` directory.

## 1. Nodus Web (Public Web Portal)

Runs the Blazor Web application where projects are registered and users can discover the event.

```powershell
cd c:\code\project_kiosko\nodusApp\src
dotnet run --project Nodus.Web/Nodus.Web.csproj
```

_Access via browser usually at `http://localhost:5158` (check your console output for the exact port)._

## 2. Nodus Server (Windows Admin Dashboard)

Runs the central server application on your Windows machine. Acts as the Swarm Base target.

```powershell
cd c:\code\project_kiosko\nodusApp\src
dotnet run --project Nodus.Server/Nodus.Server.csproj -f net10.0-windows10.0.19041.0
```

> **Note**: Verify that `Nodus.Server.exe` is not already running (zombie process) if the build fails with a "file in use" error.

## 3. Nodus Client (Android App)

Runs the voter client application. Deploys directly to a connected Android device or emulator.

```powershell
cd c:\code\project_kiosko\nodusApp\src
dotnet build Nodus.Client/Nodus.Client.csproj -t:Run -f net10.0-android
```

> **Requirements**:
>
> - Android Device connected via USB (debug mode enabled and authorized).
> - Verify connection first by running `adb devices` in the terminal. The device must NOT say `unauthorized`.

---

## 🛠️ Common Troubleshooting

If the apps fail to start:

### Clean and Rebuild

Sometimes old artifacts cause issues.

```powershell
cd c:\code\project_kiosko\nodusApp\src
dotnet clean
dotnet build
```

### Kill Zombie Server Process

If the Server fails to build because "file is in use":

```powershell
taskkill /F /IM Nodus.Server.exe
```
