FV-CORE: Digital Music Platform
====================================

DESCRIPCION
FV-CORE es una plataforma de streaming de musica y video personal basada en ASP.NET Core que sincroniza contenido directamente desde Google Drive.

CARACTERISTICAS
- Navegacion SPA-like: Sin recargas de pagina, transiciones fluidas.
- Reproduccion Persistente: El reproductor no se detiene al navegar.
- Google Drive Sync: Sincronizacion automatica de carpetas de Drive.
- Gestion de Playlists: Crear, renombrar y eliminar listas.
- Soporte PWA: Instalable en iPhone (Safari -> Agregar a pantalla de inicio).

TECNOLOGIAS
- Backend: ASP.NET Core 6.0 (MVC)
- Base de Datos: SQL Server
- Integracion: Google Drive API v3

INSTALACION
1. Clonar el repositorio.
2. Ejecutar 'dotnet restore'.
3. Configurar 'appsettings.json' (ver archivo .Example).
4. Ejecutar 'dotnet ef database update'.

CONFIGURACION
El archivo 'appsettings.json' esta ignorado por seguridad.
Usa 'appsettings.Example.json' como plantilla para configurar:
- ConnectionStrings
- GoogleDrive:FolderId
- google-credentials.json (archivo de cuenta de servicio)

NOTAS
- Use HTTPS para habilitar funciones PWA.
- PWA optimizada para iOS (Standalone mode).

(c) 2024 FV-CORE.
