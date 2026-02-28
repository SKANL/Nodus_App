# Usa el SDK de .NET 10 para compilar
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# NOTA IMPORTANTE:
# Aquí SOLO copiamos Nodus.Api, Nodus.Shared y Nodus.Infrastructure.
# La carpeta Nodus.Web es COMPLETAMENTE IGNORADA por este Dockerfile.
COPY src/Nodus.Api/Nodus.Api.csproj src/Nodus.Api/
COPY src/Nodus.Shared/Nodus.Shared.csproj src/Nodus.Shared/
COPY src/Nodus.Infrastructure/Nodus.Infrastructure.csproj src/Nodus.Infrastructure/

# Restaura las dependencias
RUN dotnet restore src/Nodus.Api/Nodus.Api.csproj

# Copia el código fuente de esos 3 proyectos (De nuevo, ignorando Nodus.Web)
COPY src/Nodus.Api/ src/Nodus.Api/
COPY src/Nodus.Shared/ src/Nodus.Shared/
COPY src/Nodus.Infrastructure/ src/Nodus.Infrastructure/

# Compila y publica en la carpeta /out
WORKDIR /app/src/Nodus.Api
RUN dotnet publish -c Release -o /out

# Usa la imagen ligera para correr la aplicación
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .

# Expone el puerto 8080 (el que usan Render y Railway por defecto)
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Comando para iniciar SOLAMENTE la API
ENTRYPOINT ["dotnet", "Nodus.Api.dll"]
