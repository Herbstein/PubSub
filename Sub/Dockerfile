FROM mcr.microsoft.com/dotnet/runtime:5.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["Sub/Sub.csproj", "Sub/"]
RUN dotnet restore "Sub/Sub.csproj"
COPY . .
WORKDIR "/src/Sub"
RUN dotnet build "Sub.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Sub.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Sub.dll"]
