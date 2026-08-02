# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy just the project file first so `dotnet restore` is cached across
# builds unless the package references themselves change.
COPY ScaleTrigger/ScaleTrigger.csproj ScaleTrigger/
RUN dotnet restore ScaleTrigger/ScaleTrigger.csproj

COPY ScaleTrigger/ ScaleTrigger/
RUN dotnet publish ScaleTrigger/ScaleTrigger.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# appsettings.json is intentionally never baked into the image (it's
# gitignored and holds secrets outside this build context anyway) - all
# configuration comes from environment variables (DatabaseProvider,
# ConnectionStrings__*, Jwt__*, ...), the same __ convention documented in
# README.md's "appsettings.json on Azure" table.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ScaleTrigger.dll"]
