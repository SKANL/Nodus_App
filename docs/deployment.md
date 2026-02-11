# 🚀 Nodus Deployment Guide

This guide covers the deployment process for the Nodus application suite, including the Server (API), Client (MAUI), and Web (Blazor WASM) components.

## Prerequisites

- .NET 8.0 SDK or later
- Docker Desktop (optional, for containerized deployment)
- Node.js (for PWA build tools, if applicable)
- A valid SSL certificate (for HTTPS)

---

## 1. Nodus.Server (API Backend)

The server acts as the central hub for data synchronization and management.

### Option A: Docker Deployment (Recommended)

1. **Build the Image:**

   ```bash
   docker build -t nodus-server -f src/Nodus.Server/Dockerfile .
   ```

2. **Run the Container:**
   ```bash
   docker run -d -p 8080:80 \
     -e "ConnectionStrings__DefaultConnection=Data Source=/app/data/nodus.db" \
     -v nodus_data:/app/data \
     --name nodus-server \
     nodus-server
   ```

### Option B: IIS / Kestrel (Windows/Linux)

1. **Publish:**

   ```bash
   dotnet publish src/Nodus.Server/Nodus.Server.csproj -c Release -o ./publish/server
   ```

2. **Configure `appsettings.json`:**
   Ensure the connection string points to a valid database location.

3. **Run:**
   ```bash
   dotnet ./publish/server/Nodus.Server.dll
   ```

---

## 2. Nodus.Web (Blazor WASM PWA)

The web portal for judges and staff. It is a Progressive Web App (PWA) capable of offline operation.

### Build & Publish

1. **Publish:**

   ```bash
   dotnet publish src/Nodus.Web/Nodus.Web.csproj -c Release -o ./publish/web
   ```

2. **Deploy to Static Host:**
   - **Azure Static Web Apps:** Use the generated `wwwroot` folder.
   - **Nginx/Apache:** Serve the `wwwroot` folder as static files.
   - **GitHub Pages:** Use the `gh-pages` branch.

### PWA Configuration

Ensure `manifest.json` and `service-worker.js` are correctly served. The PWA will automatically cache resources for offline use.

---

## 3. Nodus.Client (MAUI Mobile App)

The mobile application for offline voting and media capture.

### Android

1. **Build APK/AAB:**

   ```bash
   dotnet publish src/Nodus.Client/Nodus.Client.csproj -f net8.0-android -c Release
   ```

2. **Sign the Package:**
   Use `jarsigner` and `zipalign` to sign the `.apk` or `.aab` file with your keystore.

### iOS (Mac Required)

1. **Build IPA:**

   ```bash
   dotnet publish src/Nodus.Client/Nodus.Client.csproj -f net8.0-ios -c Release /p:ArchiveOnBuild=true
   ```

2. **Distribute:**
   Upload the `.ipa` via TestFlight or Transporter.

---

## 4. Environment Variables

| Variable                               | Description                   | Default                |
| -------------------------------------- | ----------------------------- | ---------------------- |
| `ConnectionStrings__DefaultConnection` | SQLite connection string      | `Data Source=nodus.db` |
| `Jwt__Key`                             | Secret key for JWT signing    | (Must be set in prod)  |
| `Sync__MaxUploadSize`                  | Max media upload size (bytes) | `10485760` (10MB)      |

---

## Troubleshooting

- **Database Locks:** Ensure only one process accesses the SQLite file at a time.
- **PWA Not Updating:** Clear browser cache or unregister service worker.
- **BLE Issues:** Ensure location permissions are granted on Android/iOS.
