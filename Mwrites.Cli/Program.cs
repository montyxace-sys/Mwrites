using Mwrites.Core.Documents;
using Mwrites.Portable.Core;

namespace Mwrites.Cli;

internal static class Program
{
    private const string Version = "1.1";

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h") return Help();
        if (args[0] is "--version" or "-v")
        {
            Console.WriteLine($"Mwrites CLI {Version}");
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "sample" => Sample(args),
                "info" => Info(args),
                "stats" => Stats(args),
                "convert" => Convert(args),
                "case" => ChangeCase(args),
                "find" => Find(args),
                "replace" => Replace(args),
                _ => Unknown(args[0])
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                      or NotSupportedException or InvalidDataException)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            return 1;
        }
    }

    private static int Help()
    {
        Console.WriteLine($@"Mwrites CLI {Version} — procesador de textos portable
Uso: mwrites <comando> [argumentos]

  sample [archivo]                        Crea un documento de ejemplo (.mwdoc por defecto)
  info <archivo>                          Muestra el formato, página y número de párrafos
  stats <archivo>                         Palabras, caracteres, párrafos y títulos
  convert <entrada> <salida>              Convierte entre .docx, .txt y .mwdoc
  case <entrada> <salida> <modo>          Aplica upper, lower, title o sentence
  find <archivo> <texto> [--case] [--word] Cuenta coincidencias
  replace <entrada> <salida> <texto> <nuevo> [--case]
  help, --version

Formatos: .docx (OOXML), .txt (con marcas #, >, -) y .mwdoc (nativo JSON).");
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Comando desconocido: {command}. Usa «mwrites help».");
        return 1;
    }

    private static int Sample(string[] args)
    {
        var path = args.Length > 1 ? args[1] : "ejemplo.mwdoc";
        var document = PortableDocument.Sample();
        PortableDocumentService.Save(document, path);
        Console.WriteLine($"Creado {path}");
        return 0;
    }

    private static int Info(string[] args)
    {
        if (args.Length < 2) return Missing("info <archivo>");
        var path = args[1];
        var format = Path.GetExtension(path).ToLowerInvariant().TrimStart('.');
        var document = PortableDocumentService.Open(path);
        var setup = document.Setup;
        Console.WriteLine($"Archivo:    {path}");
        Console.WriteLine($"Formato:    {format.ToUpperInvariant()}");
        Console.WriteLine($"Página:     {setup.SizeName} {(setup.Landscape ? "horizontal" : "vertical")}");
        Console.WriteLine($"Párrafos:   {document.Paragraphs.Count}");
        return 0;
    }

    private static int Stats(string[] args)
    {
        if (args.Length < 2) return Missing("stats <archivo>");
        var stats = PortableStats.Compute(PortableDocumentService.Open(args[1]));
        Console.WriteLine($"Palabras:             {stats.Words}");
        Console.WriteLine($"Caracteres:           {stats.Characters}");
        Console.WriteLine($"Caracteres sin espacio: {stats.CharactersNoSpaces}");
        Console.WriteLine($"Párrafos:             {stats.Paragraphs}");
        Console.WriteLine($"Títulos:              {stats.Headings}");
        return 0;
    }

    private static int Convert(string[] args)
    {
        if (args.Length < 3) return Missing("convert <entrada> <salida>");
        var document = PortableDocumentService.Open(args[1]);
        PortableDocumentService.Save(document, args[2]);
        Console.WriteLine($"Convertido {args[1]} → {args[2]}");
        return 0;
    }

    private static int ChangeCase(string[] args)
    {
        if (args.Length < 4) return Missing("case <entrada> <salida> <upper|lower|title|sentence>");
        var mode = args[3].ToLowerInvariant() switch
        {
            "upper" => TextCase.Upper,
            "lower" => TextCase.Lower,
            "title" => TextCase.Title,
            "sentence" => TextCase.Sentence,
            _ => throw new ArgumentException($"Modo desconocido: {args[3]}")
        };
        var document = PortableDocumentService.Open(args[1]);
        PortableFind.ApplyCase(document, mode);
        PortableDocumentService.Save(document, args[2]);
        Console.WriteLine($"Caso «{mode}» aplicado → {args[2]}");
        return 0;
    }

    private static int Find(string[] args)
    {
        if (args.Length < 3) return Missing("find <archivo> <texto> [--case] [--word]");
        var matchCase = args.Contains("--case", StringComparer.OrdinalIgnoreCase);
        var wholeWord = args.Contains("--word", StringComparer.OrdinalIgnoreCase);
        var document = PortableDocumentService.Open(args[1]);
        var matches = PortableFind.FindAll(document, args[2], matchCase, wholeWord);
        Console.WriteLine($"{matches.Count} coincidencia(s) de «{args[2]}».");
        foreach (var (paragraph, start, length) in matches.Take(20))
        {
            var text = document.Paragraphs[paragraph].Text;
            var from = Math.Max(0, start - 24);
            var snippet = text.Substring(from, Math.Min(text.Length - from, length + 48))
                .Replace('\n', ' ');
            Console.WriteLine($"  [{paragraph + 1}] …{snippet}…");
        }
        return 0;
    }

    private static int Replace(string[] args)
    {
        if (args.Length < 5) return Missing("replace <entrada> <salida> <texto> <nuevo> [--case]");
        var document = PortableDocumentService.Open(args[1]);
        var count = PortableFind.ReplaceAll(document, args[3], args[4],
            args.Contains("--case", StringComparer.OrdinalIgnoreCase));
        PortableDocumentService.Save(document, args[2]);
        Console.WriteLine($"{count} reemplazo(s) → {args[2]}");
        return 0;
    }

    private static int Missing(string usage)
    {
        Console.Error.WriteLine($"Faltan argumentos. Uso: mwrites {usage}");
        return 1;
    }
}
