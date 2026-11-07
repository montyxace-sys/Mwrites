# Mwrites

Procesador de textos para Windows con la apariencia de Microsoft Word y un flujo de trabajo pensado para escribir sin distracciones. Es gratuito, open source y funciona sin conexión: todos los documentos se guardan en tu equipo.

## Funciones

### Documentos
- Abrir y guardar en **.docx**, **.rtf** y **.txt**, conservando títulos, listas, tablas, imágenes y saltos de página.
- Documento nuevo con estilos listos para usar, lista de archivos recientes e impresión.
- Aviso de cambios sin guardar al cerrar o abrir otro documento.

### Edición de texto
- Fuente, tamaño, negrita, cursiva, subrayado, tachado, subíndice y superíndice.
- Color de letra y resaltado.
- Alineación, sangría, viñetas, listas numeradas e interlineado.
- Deshacer y rehacer, cortar, copiar, pegar y seleccionar todo.
- Buscar y reemplazar, con reemplazo de todas las apariciones.

### Estilos
- Normal, Título 1, Título 2, Título 3, Subtítulo y Cita.
- El panel de navegación muestra los títulos del documento para saltar entre secciones.
- Al pulsar Intro al final de un título, el párrafo siguiente vuelve al estilo Normal.

### Tablas
- Insertar tablas de hasta 30 filas y 12 columnas, con encabezado sombreado.
- Barra contextual con **Fila debajo**, **Eliminar fila**, **Columna derecha**, **Eliminar columna** y **Eliminar tabla**.
- Tab recorre las celdas y, en la última, añade una fila nueva; Mayús+Tab vuelve a la anterior.
- Las columnas se reparten el ancho de la página para no salirse de los márgenes.

### Páginas
- Tamaños A4, Carta, Oficio y Ejecutivo, orientación vertical u horizontal y márgenes configurables.
- Salto de página visible con una marca discontinua y una etiqueta, como en Word.
- Contador de palabras y de páginas, regla y líneas de cuadrícula.

### Vistas
- Modo lectura y modo sin distracciones (Esc para salir).
- Zoom al 100 % o ajustado al ancho de la página.
- Corrector ortográfico en español (España y México), inglés y francés, con sugerencias al hacer clic derecho.

## Descargar

- **MwritesSetup.exe**: instalador para Windows 10/11 de 64 bits. Crea accesos directos, entrada en Aplicaciones instaladas y un desinstalador.
- **Mwrites.exe**: versión portable, sin instalación. No necesita .NET: es un único ejecutable autocontenido.

Ambos están en la sección de releases del repositorio.

## Compilar

Requisitos: Windows y el SDK de .NET 8.

```powershell
# Aplicación
dotnet build Mwrites.slnx -c Release

# Ejecutable portable autocontenido
dotnet publish App/Mwrites.App.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish

# Instalador (genera publish\MwritesSetup.exe)
.\build-setup.ps1
```

## Estructura

- `App/`: interfaz WPF, cinta de opciones, panel de navegación y diálogos.
- `Core/`: modelo del documento, lectura y escritura de .docx, estilos, búsqueda y estadísticas.
- `tests/`: pruebas del núcleo.
- `Installer/`: instalador y desinstalador.

## Licencia

MIT. Consulta [LICENSE](LICENSE).
