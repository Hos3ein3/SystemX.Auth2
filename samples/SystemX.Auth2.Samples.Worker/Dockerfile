FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Copy props and dependent projects
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/SystemX.Auth2.Contracts/ src/SystemX.Auth2.Contracts/
COPY src/SystemX.Auth2.Client/ src/SystemX.Auth2.Client/
COPY samples/SystemX.Auth2.Samples.Worker/ samples/SystemX.Auth2.Samples.Worker/

WORKDIR /repo/samples/SystemX.Auth2.Samples.Worker
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SystemX.Auth2.Samples.Worker.dll"]
