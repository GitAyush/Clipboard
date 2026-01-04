FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ClipboardSync.sln ./
COPY shared/ClipboardSync.Protocol/ClipboardSync.Protocol.csproj shared/ClipboardSync.Protocol/
COPY server/RelayServer/RelayServer.csproj server/RelayServer/

RUN dotnet restore server/RelayServer/RelayServer.csproj

COPY shared/ shared/
COPY server/RelayServer/ server/RelayServer/

RUN dotnet publish server/RelayServer/RelayServer.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out ./

ENV ASPNETCORE_URLS=http://0.0.0.0:5104
EXPOSE 5104

ENTRYPOINT ["dotnet","RelayServer.dll"]


