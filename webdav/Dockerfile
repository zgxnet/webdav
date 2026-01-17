FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY webdav/webdav.csproj webdav/
RUN dotnet restore webdav/webdav.csproj

# Copy source code and build
COPY webdav/ webdav/
WORKDIR /src/webdav
RUN dotnet build -c Release -o /app/build
RUN dotnet publish -c Release -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# Create data directory
RUN mkdir -p /data

# Copy published app
COPY --from=build /app/publish .

# Expose default port
EXPOSE 6065

# Set environment
ENV ASPNETCORE_URLS=http://+:6065
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "webdav.dll"]
