# Use official .NET ASP.NET runtime as base image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

# Use .NET SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ./AlchemyCallbackTest ./AlchemyCallbackTest
WORKDIR /src/AlchemyCallbackTest
RUN dotnet publish -c Release -o /app

# Final runtime image
FROM base AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080}
ENTRYPOINT ["dotnet", "AlchemyCallbackTest.dll"]
