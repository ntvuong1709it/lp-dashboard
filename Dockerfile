# Single image: React build is served by the .NET API from wwwroot.

FROM node:22-alpine AS frontend
WORKDIR /src
COPY frontend/package.json ./
RUN npm install
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY backend/ ./
RUN dotnet publish src/LpDashboard.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=backend /app ./
COPY --from=frontend /src/dist ./wwwroot
# The base image already listens on 8080 (HTTP_PORTS).
ENV ConnectionStrings__Default="Data Source=/data/lpdashboard.db"
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "LpDashboard.Api.dll"]
