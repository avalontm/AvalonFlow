# AvalonFlow REST API

Bienvenido a la documentación de la API REST de **AvalonFlow**. Este proyecto implementa un framework HTTP minimalista basado en `HttpListener` para construir APIs REST modernas en C#.

---

## 📚 Características

* Ruteo mediante atributos (\[HttpGet], \[HttpPost], etc.).
* Inyección de parámetros y deserialización automática.
* Manejo de respuestas con `ActionResult`, `FileActionResult` y `StreamFileActionResult`.
* Soporte para descarga y streaming de archivos.
* Sistema de controladores tipo MVC.
* Seguridad por token personalizada.

---

## ⚙️ Ejemplo de uso

```text
using AvalonFlow;
using AvalonFlow.Rest;
using AvalonFlowRest.Services;

namespace AvalonFlowRest
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Opcional: puedes leer puerto de args o config
            int port = 5000;

            var server = new AvalonRestServer(port);
            // al iniciar el servidor
            AvalonServiceRegistry.RegisterSingleton<EmailService>(new EmailService());

            await server.StartAsync(); // Corre el servidor indefinidamente
        }
    }
}

```

---

## 🚀 Ejecución Rápida

```bash
# Ejecutar desde consola
$ dotnet run
```

El servidor correrá por defecto en:

```
http://localhost:5000/
```

---

## 📂 Endpoints

### 💾 Descargar Archivo

**GET** `/api/file/download`

Descarga un archivo como attachment.

```http
GET /api/file/download HTTP/1.1
```

### 🎥 Ver Video (Streaming)

**GET** `/api/file/video`

Stream de un archivo de video.

```http
GET /api/file/video HTTP/1.1
```

### 🖼️ Ver Imagen

**GET** `/api/file/image`

Renderiza la imagen inline.

```http
GET /api/file/image HTTP/1.1
```

> Se recomienda mostrarla en HTML con `img` y `pointer-events: none` para evitar interacciones.

---

## 🧭 Uso de Controllers

```csharp
[AvalonController("api/[controller]")]
public class FileController : AvalonControllerBase
{
    [HttpGet("download")]
    public ActionResult DownloadFile() => ...;
}
```

* `[AvalonController]`: Define la ruta base del controller.
* Hereda de `AvalonControllerBase` para acceso a métodos como `Ok()`, `NotFound()`, `File()`, etc.

### Métodos soportados:

* `[HttpGet("ruta")]`
* `[HttpPost("ruta")]`
* `[HttpPut("ruta")]`
* `[HttpDelete("ruta")]`
* `[HttpPatch("ruta")]`
* `[HttpOptions("ruta")]`

### Inyección de parámetros

```csharp
[HttpGet("usuario/{id}")]
public ActionResult GetUsuario(int id)
{
    // id es inyectado desde la ruta
}

[HttpPost("crear")]
public ActionResult CrearUsuario(UsuarioDto dto)
{
    // dto es deserializado automáticamente desde el body JSON
}
```

---

## 🔐 Seguridad y Autenticación

AvalonFlow permite validar manualmente las peticiones antes de ejecutar los controladores:

```csharp
if (!context.Request.Headers.TryGetValue("Authorization", out var token) || !ValidateToken(token))
{
    await RespondWith(context, 401, new { error = "Unauthorized" });
    return;
}
```

También puedes aplicar autorización por controlador o endpoint:

```csharp
[HttpGet("secure")]
public ActionResult GetProtectedFile()
{
    if (!HttpContext.User?.IsAuthenticated ?? true)
        return Unauthorized("Token inválido");
    // lógica segura
}
```

> Puedes extender `AvalonHttpContext` para guardar info del usuario autenticado.

---

## 🛡️ Seguridad para Archivos

Para proteger el acceso a archivos y multimedia:

* ✅ Requiere autenticación JWT o tokens temporales.
* ✅ Evita `Content-Disposition: attachment` si no deseas descargas.
* ✅ Implementa `token` o `exp` para URLs seguras.
* ✅ Usa marcas de agua si es contenido sensible.
* ✅ Añade headers:

```csharp
context.Response.AddHeader("Cache-Control", "no-store");
context.Response.AddHeader("Pragma", "no-cache");
context.Response.AddHeader("Content-Security-Policy", "default-src 'none';");
```

---

## 🔧 Ejemplo de Uso

```csharp
[HttpGet("image")]
public ActionResult GetImage()
{
    var path = Path.Combine(Directory.GetCurrentDirectory(), "files", "imagen.jpg");
    if (!System.IO.File.Exists(path)) return NotFound("Imagen no encontrada.");
    var stream = System.IO.File.OpenRead(path);
    return new StreamFileActionResult(stream, "image/jpeg", "imagen.jpg", isAttachment: false);
}
```

---

## ✨ Contribuciones

Tus contribuciones son bienvenidas. Abre un Pull Request o issue para colaborar.

---

## 📄 Licencia

Este proyecto está licenciado bajo MIT. Consulta `LICENSE` para más detalles.
