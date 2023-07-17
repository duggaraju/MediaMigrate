#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:6.0-jammy AS base
WORKDIR /app
RUN apt-get update && apt-get install -y xz-utils
ADD https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linux64-lgpl.tar.xz /
RUN tar xf /ffmpeg-master-latest-linux64-lgpl.tar.xz -C /usr/local --strip=1

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["MediaMigrate.csproj", "."]
RUN dotnet restore "./MediaMigrate.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "MediaMigrate.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MediaMigrate.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
LABEL version="1.0" maintainer="Prakash Duggarju <duggaraju@gmail.com>"
WORKDIR /app
COPY --from=publish /app/publish .
RUN chmod +x /app/packager-linux-x64
ENTRYPOINT ["dotnet", "MediaMigrate.dll"]