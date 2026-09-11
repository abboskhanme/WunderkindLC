# ============================================================================
#  WunderkindLC — bitta o'quv markazi uchun yagona obraz (API + SPA)
#  3 bosqich: (1) SPA build (node) -> (2) API publish (.NET 8) -> (3) runtime
# ============================================================================

# ---------- 1) Frontend (Vite) build ----------
FROM node:20-alpine AS client
WORKDIR /client
COPY WunderkindLC.Client/package*.json ./
RUN npm ci
COPY WunderkindLC.Client/ ./
# Build-time env: frontend real domenni tanishi va REAL API'ga (mock emas) ulanishi uchun.
ARG VITE_ROOT_DOMAIN=wunderkindlc.uz
ARG VITE_USE_MOCK=false
# PostHog (frontend analytics) — kalit OMMAVIY (brauzer bundle'ida ko'rinadi), maxfiy emas.
ARG VITE_POSTHOG_KEY=
ARG VITE_POSTHOG_HOST=
ENV VITE_ROOT_DOMAIN=$VITE_ROOT_DOMAIN VITE_USE_MOCK=$VITE_USE_MOCK \
    VITE_POSTHOG_KEY=$VITE_POSTHOG_KEY VITE_POSTHOG_HOST=$VITE_POSTHOG_HOST
# Node HEAP chegarasi: konteynerda Node o'zi ~512 MB "old space" tanlab oladi va loyiha
# o'sgani sayin `tsc -b && vite build` o'sha chegarada "heap out of memory" (exit 134) bilan
# yiqiladi. Aniq belgilab qo'yamiz. DIQQAT: Docker VM'ining o'zida ham shuncha RAM bo'lishi
# kerak (Docker Desktop → Settings → Resources → Memory ≥ 6 GB).
ENV NODE_OPTIONS=--max-old-space-size=4096
RUN npm run build        # natija: /client/dist

# ---------- 2) Backend (.NET) publish ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# Avval faqat csproj'lar — qatlam keshini saqlash uchun. Klient (esproj) Docker'da qurilmaydi.
COPY WunderkindLC.Domain/WunderkindLC.Domain.csproj WunderkindLC.Domain/
COPY WunderkindLC.Application/WunderkindLC.Application.csproj WunderkindLC.Application/
COPY WunderkindLC.Infrastructure/WunderkindLC.Infrastructure.csproj WunderkindLC.Infrastructure/
COPY WunderkindLC.Server/WunderkindLC.Server.csproj WunderkindLC.Server/
RUN dotnet restore WunderkindLC.Server/WunderkindLC.Server.csproj -p:BuildSpa=false
COPY WunderkindLC.Domain/ WunderkindLC.Domain/
COPY WunderkindLC.Application/ WunderkindLC.Application/
COPY WunderkindLC.Infrastructure/ WunderkindLC.Infrastructure/
COPY WunderkindLC.Server/ WunderkindLC.Server/
RUN dotnet publish WunderkindLC.Server/WunderkindLC.Server.csproj -c Release -o /app/publish \
    -p:BuildSpa=false --no-restore

# ---------- 3) Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
# Markaz mintaqasi (UTC+5). tzdata — TimeZoneInfo "Asia/Tashkent"ni topishi uchun;
# TZ — log/uchinchi-tomon kutubxonalar ham mahalliy vaqtda bo'lishi uchun.
ENV TZ=Asia/Tashkent
# SERTIFIKAT: Word (.docx) andozasini PDF ga o'girish uchun LibreOffice kerak — u sahifani
# AYNAN Word'dagidek chizadi (bepul .NET kutubxonalari buni uddalay olmaydi).
#   • `libreoffice-writer` — to'liq LibreOffice EMAS, faqat matn hujjatlari moduli (~250-300 MB).
#   • `fonts-dejavu`/`fonts-liberation` — shriftsiz PDF'da o'zbek/rus harflari kvadratga aylanadi;
#     `fonts-liberation` Arial/Times ga metrik jihatdan mos (andozalar odatda shularda yoziladi).
#   • `--no-install-recommends` — java va boshqa og'ir tavsiyalar tortilmasin.
# DIQQAT (1 GB RAM server): bitta konvertatsiya ~150-200 MB oladi, shuning uchun kod ularni
# NAVBAT bilan bajaradi (DocxToPdfConverter). LibreOffice bo'lmasa tizim ishdan chiqmaydi —
# sertifikat .docx sifatida saqlanadi va foydalanuvchiga ogohlantirish ko'rsatiladi.
# FFMPEG: NVR arxividan klip olish uchun (RTSP -> MP4). Qayta kodlash YO'Q, faqat remux
# (`-c copy`) — CPU/RAM deyarli sarflanmaydi. Busiz "Yozuvni ko'rish" NVR rejimida ishlamaydi.
RUN apt-get update && apt-get install -y --no-install-recommends \
        tzdata ffmpeg libreoffice-writer libreoffice-core fonts-dejavu fonts-liberation \
    && ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone \
    && rm -rf /var/lib/apt/lists/*
# LibreOffice o'z profilini HOME ichida yaratadi; konteynerda HOME bo'lmasa xato beradi.
ENV HOME=/tmp
COPY --from=build /app/publish ./
# Qurilgan SPA'ni server statik papkasiga qo'yamiz (API ham, SPA ham bitta originda).
COPY --from=client /client/dist ./wwwroot
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "WunderkindLC.Server.dll"]
