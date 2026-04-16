# FV-CORE

**Plataforma de Streaming Personal**

FV-CORE es una plataforma de streaming multimedia local desarrollada con ASP.NET Core MVC (.NET 8). Su objetivo es proporcionar una interfaz unificada, rapida y esteticamente disfrutable (basada en el estilo Cyberpunk) para organizar y reproducir archivos multimedia locales.

## Caracteristicas Principales

*   **Scanner Automatico al Iniciar:** Servicio en segundo plano que indexa la coleccion de musica y videos de la ruta fisica especificada sin intervencion del usuario.
*   **Soporte para Multiples Formatos:** Escaneo e identificacion de archivos de audio interactivos (.mp3, .m4a, .wma, .wav, .flac) y video (.mp4, .mkv, .avi, .mov).
*   **Metadata y Portadas:** Extraccion y vinculacion automatica de imagenes `cover.jpg` o `folder.jpg` en las carpetas. Genera imagenes SVG predeterminadas si no se detecta portada.
*   **Clasificador de Genero mediante IA:** Servicio integrado que procesa los titulos y artistas para asignar categorias primarias.
*   **Reproductor de Audio Persistente:** Barra de reproduccion HTML5 ininterrumpida que se mantiene reproduciendo la musica mientras se navega por la interfaz.
*   **Visor de Video Inline:** Componente integrado para visualizar contenido de video directamente sobre el dashboard.
*   **Modulo de Listas de Reproduccion (Playlists):**
    *   Seleccion multiple iterativa de pistas.
    *   Creacion nativa de colecciones a demanda (via ventana tipo terminal interactiva).
    *   Adicion rapida y reproducciones parciales.
*   **Funcionalidad de Reproduccion Aleatoria (Shuffle):** Algoritmo de barajado incorporado para reproducir artistas, listas o librerias enteras al azar.
*   **Sistema de Favoritos:** Herramienta agil para marcar canciones favoritas y agregarlas instantaneamente a una seleccion de facil acceso.
*   **Interfaz Grafica Cyberpunk:** Desarrollada sin utilitarios extra de CSS, puramente disenada en vanilla CSS con una profunda integracion de estilizacion dark-mode centrada en acentos Verde Neon y Morado Electrico.

## Requisitos Tecnicos

*   .NET 8.0 SDK
*   SQL Server LocalDB (para la base de datos Entity Framework Core)
*   Navegador web moderno (Chrome, Firefox, o Edge)

## Instrucciones de Instalacion y Ejecucion

1.  Abra una terminal (PowerShell o CMD) en el directorio raiz del proyecto.
2.  Ejecute `dotnet build` para restaurar los paquetes NuGet y compilar la solucion.
3.  Cree y actualice la base de datos ejecutando los comandos de migracion:
    *   `dotnet ef migrations add InitialCreate`
    *   `dotnet ef database update`
4.  Inicie el servidor web ejecutando:
    *   `dotnet run`
5.  La aplicacion estara disponible localmente en `http://localhost:5100`. Al iniciar por primera vez, el Scanner indexara todos los medios que encuentre en el sistema, puede visualizar el estado en la misma ventana de comandos.

## Estructura del Proyecto

*   `/Controllers`: Controladores principales que administran las secciones Home, Artists, Media, Playlists, Favorites, Cover y Streaming.
*   `/Models`: Dominio de la aplicacion que contiene las entidades MediaItem, Artist y las correlaciones para Playlist.
*   `/Services`: Contiene el Background Service del scanner de archivos y la implementacion base del clasificador por genero.
*   `/Data`: Contexto de Base de datos SQL.
*   `/Views`: HTML Renderizado usando Razor en estetica Neon.
*   `/wwwroot`: Archivos CSS, JS estaticos e imagenes necesarias para la carga visual.

## Licencia de Uso

Uso estrictamente personal y local. Proyecto creado por la iniciativa FV-CORE.
