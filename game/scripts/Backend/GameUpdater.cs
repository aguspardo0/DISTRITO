using Godot;

namespace Distrito.Game.Backend;

/// <summary>
/// Actualización automática del cliente de Windows. Al abrir el juego compara su versión (la escribe la CI en
/// server_defaults.json) con la última publicada en la release "prototipo". Si hay una nueva, la descarga,
/// la descomprime, cierra el juego y un script de PowerShell copia los archivos y lo vuelve a abrir.
/// Si algo falla, el juego sigue con la versión actual y avisa dónde descargarla a mano.
/// </summary>
public partial class GameUpdater : Node
{
    public const string ReleaseBase = "https://github.com/aguspardo0/DISTRITO/releases/download/prototipo";
    private const string ZipName = "DISTRITO-windows.zip";

    private string _current = "";
    private string _latest = "";
    private HttpRequest _http = null!;
    private string _workDir = "";

    /// <summary>Mensajes para el menú.</summary>
    public event Action<string>? Status;
    /// <summary>true: está descargando o instalando (el menú se bloquea). false: terminó sin actualizar.</summary>
    public event Action<bool>? Busy;

    public static bool Supported => OS.HasFeature("windows") && OS.HasFeature("template");

    public void Start(string currentVersion)
    {
        _current = currentVersion.Trim();
        if (!Supported || _current.Length == 0)
            return;
        _workDir = System.IO.Path.Combine(OS.GetUserDataDir(), "actualizacion");
        _http = new HttpRequest { MaxRedirects = 10, Timeout = 15 };
        AddChild(_http);
        _http.RequestCompleted += OnVersionReceived;
        if (_http.Request($"{ReleaseBase}/version.txt") != Error.Ok)
            GD.Print("[DISTRITO] No se pudo consultar si hay una versión nueva.");
    }

    private void OnVersionReceived(long result, long code, string[] headers, byte[] body)
    {
        _http.RequestCompleted -= OnVersionReceived;
        if (result != (long)HttpRequest.Result.Success || code != 200)
            return; // sin internet o sin release: se juega con la versión actual
        _latest = System.Text.Encoding.UTF8.GetString(body).Trim();
        if (_latest.Length == 0 || _latest == _current)
            return;

        if (!CanWriteGameFolder())
        {
            Status?.Invoke($"Hay una versión nueva, pero no puedo escribir en la carpeta del juego. Descargala de {ReleaseBase}/{ZipName}");
            return;
        }

        Busy?.Invoke(true);
        Status?.Invoke($"Hay una versión nueva ({_latest}). Descargando…");
        System.IO.Directory.CreateDirectory(_workDir);
        _http.DownloadFile = System.IO.Path.Combine(_workDir, ZipName);
        _http.Timeout = 600;
        _http.RequestCompleted += OnZipDownloaded;
        _http.Request($"{ReleaseBase}/{ZipName}");
    }

    public override void _Process(double delta)
    {
        if (_http is null || _http.GetHttpClientStatus() != HttpClient.Status.Body || _http.GetBodySize() <= 0)
            return;
        var pct = 100L * _http.GetDownloadedBytes() / _http.GetBodySize();
        Status?.Invoke($"Descargando la versión nueva ({_latest})… {pct} %");
    }

    private void OnZipDownloaded(long result, long code, string[] headers, byte[] body)
    {
        _http.RequestCompleted -= OnZipDownloaded;
        if (result != (long)HttpRequest.Result.Success || code != 200)
        {
            Fail($"No se pudo descargar la versión nueva (error {code}).");
            return;
        }
        try
        {
            var files = System.IO.Path.Combine(_workDir, "archivos");
            if (System.IO.Directory.Exists(files))
                System.IO.Directory.Delete(files, recursive: true);
            Extract(System.IO.Path.Combine(_workDir, ZipName), files);
            if (!System.IO.File.Exists(System.IO.Path.Combine(files, "DISTRITO.exe")))
            {
                Fail("La descarga no tiene el juego adentro.");
                return;
            }
            Status?.Invoke("Instalando la versión nueva… el juego se va a reabrir solo.");
            Install(files);
        }
        catch (Exception ex)
        {
            Fail($"No se pudo instalar la versión nueva: {ex.Message}");
        }
    }

    /// <summary>Descomprime quitando la carpeta raíz del zip ("windows/").</summary>
    private static void Extract(string zipPath, string destination)
    {
        var zip = new ZipReader();
        if (zip.Open(zipPath) != Error.Ok)
            throw new InvalidOperationException("el archivo descargado está dañado");
        try
        {
            foreach (var entry in zip.GetFiles())
            {
                if (entry.EndsWith('/'))
                    continue;
                var slash = entry.IndexOf('/');
                var relative = slash >= 0 ? entry[(slash + 1)..] : entry;
                if (relative.Length == 0 || relative.Contains(".."))
                    continue;
                var target = System.IO.Path.Combine(destination, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                System.IO.File.WriteAllBytes(target, zip.ReadFile(entry));
            }
        }
        finally
        {
            zip.Close();
        }
    }

    private void Install(string files)
    {
        var gameDir = System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!;
        var script = System.IO.Path.Combine(_workDir, "instalar.ps1");
        System.IO.File.WriteAllText(script, """
            param([string]$Src, [string]$Dst, [int]$ProcId)
            try { Wait-Process -Id $ProcId -Timeout 60 -ErrorAction SilentlyContinue } catch {}
            Start-Sleep -Milliseconds 800
            for ($i = 0; $i -lt 10; $i++) {
              try { Copy-Item -Path (Join-Path $Src '*') -Destination $Dst -Recurse -Force -ErrorAction Stop; break }
              catch { Start-Sleep -Seconds 1 }
            }
            Start-Process -FilePath (Join-Path $Dst 'DISTRITO.exe') -WorkingDirectory $Dst
            """);
        var pid = OS.CreateProcess("powershell.exe",
        [
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
            "-Src", files, "-Dst", gameDir, "-ProcId", OS.GetProcessId().ToString(System.Globalization.CultureInfo.InvariantCulture),
        ]);
        if (pid <= 0)
        {
            Fail("No se pudo iniciar el instalador.");
            return;
        }
        GetTree().Quit();
    }

    private static bool CanWriteGameFolder()
    {
        try
        {
            var probe = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!, ".prueba-escritura");
            System.IO.File.WriteAllText(probe, "ok");
            System.IO.File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void Fail(string message)
    {
        GD.PushWarning($"[DISTRITO] {message}");
        Status?.Invoke($"{message} Podés seguir jugando con esta versión o descargarla de {ReleaseBase}/{ZipName}");
        Busy?.Invoke(false);
    }
}
