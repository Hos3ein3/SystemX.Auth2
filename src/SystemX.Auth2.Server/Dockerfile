FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# Node builds the Tailwind stylesheet (the MSBuild target Auth2Tailwind runs npm ci and the CSS build; the build fails on an error).
RUN apt-get update && apt-get install -y --no-install-recommends nodejs npm && rm -rf /var/lib/apt/lists/*
WORKDIR /repo

# Copy props and shared files
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/SystemX.Auth2.Contracts/ src/SystemX.Auth2.Contracts/
COPY src/SystemX.Auth2.Server/ src/SystemX.Auth2.Server/

WORKDIR /repo/src/SystemX.Auth2.Server
RUN dotnet publish -c Release -o /app/publish -p:NoWarn=1591

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "SystemX.Auth2.Server.dll"]
