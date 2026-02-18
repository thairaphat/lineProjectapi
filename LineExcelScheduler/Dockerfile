FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY *.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0

WORKDIR /app
RUN apt-get update && apt-get install -y \
    libgdiplus \
    libx11-6 \
    libxcb1 \
    libxext6 \
    libxrender1 \
    libssl-dev \
    fontconfig \
    xfonts-75dpi \
    fonts-thai-tlwg \
    xfonts-base \
    && rm -rf /var/lib/apt/lists/* 
COPY --from=build /app .
COPY wwwroot/libwkhtmltox.so /app/wwwroot/libwkhtmltox.so
ENV LD_LIBRARY_PATH="/app/wwwroot:/usr/lib:${LD_LIBRARY_PATH:-}"
# COPY wwwroot/libwkhtmltox.so /usr/local/lib/libwkhtmltox.so
EXPOSE 8080
ENTRYPOINT ["dotnet", "LineExcelScheduler.dll"]
