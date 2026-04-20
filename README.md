# FV-CORE: Digital Music Platform

FV-CORE es una plataforma de streaming de música y video personal basada en la web, diseñada para la máxima fluidez y rendimiento. Permite sincronizar y reproducir contenido multimedia directamente desde Google Drive con una interfaz moderna y una experiencia de navegación sin interrupciones.

## 🚀 Características Principales

- **Navegación Fluida (SPA-like)**: Sistema de navegación basado en AJAX que elimina el parpadeo visual. Los cambios de vista son suaves (fade-in/fade-out) y el reproductor de música es persistente.
- **Sincronización con Google Drive**: Integración robusta con `ScannerService` para indexar contenido de forma recursiva desde una carpeta específica de Google Drive.
- **Reproductor Persistente**: Control de reproducción global que se mantiene activo durante toda la sesión de navegación.
- **Gestión de Playlists**: Sistema completo para crear, editar nombres y eliminar listas de reproducción de forma dinámica.
- **Soporte PWA (iOS Optimized)**: Instalable en iPhone (Add to Home Screen) con soporte para modo *standalone*, iconos de alta resolución y visualización en pantalla completa.
- **Búsqueda Instantánea**: Filtrado en tiempo real de artistas, álbumes y canciones.

## 🛠 Tecnologías Usadas

- **Backend**: ASP.NET Core 6.0 (MVC)
- **Base de Datos**: Entity Framework Core con SQL Server
- **Frontend**: JavaScript Vanilla (ES6+), CSS3 con variables de diseño (Neon Aesthetic)
- **APIs**: Integración con Google Drive API v3
- **DevOps**: Registro de Service Workers para soporte PWA

## 📦 Instalación

Para configurar una instancia local de FV-CORE, sigue estos pasos:

1. **Clonar el repositorio**:
   ```bash
   git clone https://github.com/FELIPE8406/FV-CORE.git
   ```

2. **Restaurar dependencias**:
   Asegúrate de tener el SDK de .NET instalado y ejecuta:
   ```bash
   dotnet restore
   ```

3. **Configurar la base de datos**:
   Actualiza la cadena de conexión en tu archivo de configuración (ver sección de Configuración).
   Ejecuta las migraciones para crear las tablas necesarias:
   ```bash
   dotnet ef database update
   ```

## ⚙️ Configuración

Por razones de seguridad, el archivo de configuración real (`appsettings.json`) está excluido del repositorio. Debes crear uno basado en la plantilla de ejemplo:

1. Localiza el archivo `appsettings.Example.json`.
2. Renómbralo o cópialo como `appsettings.json`.
3. Completa los valores requeridos:
   - **DefaultConnection**: Tu cadena de conexión a SQL Server.
   - **GoogleDrive:FolderId**: El ID de la carpeta de Drive que deseas escanear.
   - **google-credentials.json**: Coloca tu archivo de credenciales de Google Service Account en el directorio raíz.

## 📱 Uso y PWA

### Sincronización
Para añadir música a la plataforma:
- Sube tus archivos a la carpeta de Google Drive configurada.
- En la aplicación, ve a la sección de configuración o usa el botón **Sincronizar con Drive**.

### Instalación en iPhone
Para disfrutar de la experiencia completa en iOS:
1. Abre la URL en **Safari**.
2. Toca el botón **Compartir**.
3. Selecciona **"Agregar a pantalla de inicio"**.
4. La app se abrirá en modo *standalone* (sin barras de navegador) con el icono dedicado.

## 📝 Notas Técnicas

- **Seguridad**: El sistema requiere HTTPS para habilitar todas las funciones PWA en dispositivos móviles.
- **Service Worker**: El archivo `sw.js` opera en modo passthrough (sin caché agresivo) para garantizar la compatibilidad inmediata con el sistema de navegación dinámica.

---
© 2024 FV-CORE. Desarrollado con enfoque en el rendimiento y la estética Cyberpunk.
