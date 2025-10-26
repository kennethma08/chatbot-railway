# ===== build =====
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "WhatsappClient/WhatsappClient.csproj"
RUN dotnet publish "WhatsappClient/WhatsappClient.csproj" -c Release -o /app/publish

# ===== runtime =====
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
CMD ["sh","-c","dotnet WhatsappClient.dll --urls http://0.0.0.0:"]