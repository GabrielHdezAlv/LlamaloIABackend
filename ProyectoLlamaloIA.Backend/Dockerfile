# Etapa 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copiamos el archivo .csproj y restauramos dependencias
COPY *.csproj ./
RUN dotnet restore

# Copiamos el resto del código y publicamos
COPY . ./
RUN dotnet publish -c Release -o /app/publish

# Etapa 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Copiamos la app publicada desde la etapa anterior
COPY --from=build /app/publish .

# Render usa la variable PORT, la app debe escuchar en ese puerto
ENV ASPNETCORE_URLS=http://+:${PORT:-5000}

# Comando para ejecutar tu aplicación
ENTRYPOINT ["dotnet", "ProyectoLlamaloIA.Backend.dll"]
