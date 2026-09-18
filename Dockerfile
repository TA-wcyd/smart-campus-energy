# Stage 1: Build stage with .NET 10 SDK
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first for optimal dependency caching
COPY ["MyApp.Api/MyApp.Api.csproj", "MyApp.Api/"]
COPY ["MyApp.Core/MyApp.Core.csproj", "MyApp.Core/"]
COPY ["MyApp.Infrastructure/MyApp.Infrastructure.csproj", "MyApp.Infrastructure/"]

# Restore dependencies
RUN dotnet restore "MyApp.Api/MyApp.Api.csproj"

# Copy the remaining source files and build
COPY . .
WORKDIR "/src/MyApp.Api"
RUN dotnet publish "MyApp.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Final runtime stage with ASP.NET 10 runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Copy published application from build stage
COPY --from=build /app/publish .

# Configure default port for container runtime
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MyApp.Api.dll"]
