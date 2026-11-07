using System.Windows;
using System.Windows.Documents;

namespace Mwrites.Core.Documents;

public static class Templates
{
    public sealed record Template(string Name, string Description, Func<PageSetup, FlowDocument> Create);

    public static readonly Template[] All =
    {
        new("Documento en blanco", "Empieza con una página vacía.", Blank),
        new("Carta", "Carta formal con fecha, saludo y despedida.", Letter),
        new("Informe", "Informe con portada, secciones y viñetas.", Report),
        new("Currículum", "Currículum con experiencia y formación.", Resume)
    };

    public static FlowDocument Blank(PageSetup setup)
    {
        var document = DocumentStyles.CreateDocument(setup);
        document.Blocks.Add(DocumentStyles.CreateParagraph(document));
        return document;
    }

    public static FlowDocument Letter(PageSetup setup)
    {
        var document = DocumentStyles.CreateDocument(setup);
        Add(document, DocumentStyles.Normal, DateTime.Today.ToString("d 'de' MMMM 'de' yyyy"));
        Add(document, DocumentStyles.Normal, string.Empty);
        Add(document, DocumentStyles.Normal, "Estimado/a [nombre]:");
        Add(document, DocumentStyles.Normal, string.Empty);
        Add(document, DocumentStyles.Normal,
            "Escriba aquí el motivo de su carta. Puede contar los detalles con calma y cerrar con una petición clara.");
        Add(document, DocumentStyles.Normal, string.Empty);
        Add(document, DocumentStyles.Normal, "Quedo a su disposición para cualquier aclaración.");
        Add(document, DocumentStyles.Normal, string.Empty);
        Add(document, DocumentStyles.Normal, "Un saludo,");
        Add(document, DocumentStyles.Normal, string.Empty);
        Add(document, DocumentStyles.Normal, "[Su nombre]");
        return document;
    }

    public static FlowDocument Report(PageSetup setup)
    {
        var document = DocumentStyles.CreateDocument(setup);
        Add(document, DocumentStyles.Title, "Informe de proyecto");
        Add(document, DocumentStyles.Subtitle, "Un resumen claro de lo que se ha hecho y de lo que viene después");
        Add(document, DocumentStyles.Heading1, "Resumen");
        Add(document, DocumentStyles.Normal,
            "Explique en dos o tres frases el objetivo del trabajo, el resultado principal y la decisión que se propone.");
        Add(document, DocumentStyles.Heading1, "Contexto");
        Add(document, DocumentStyles.Normal,
            "Describa el punto de partida, las personas implicadas y las restricciones que condicionan la solución.");
        Add(document, DocumentStyles.Heading2, "Datos principales");
        AddBullets(document,
            "Qué se ha medido y con qué método.",
            "Qué cifras sostienen la conclusión.",
            "Qué queda fuera del alcance de este informe.");
        Add(document, DocumentStyles.Heading1, "Conclusiones");
        Add(document, DocumentStyles.Normal,
            "Cierre con las conclusiones y los siguientes pasos, cada uno con un responsable y una fecha.");
        Add(document, DocumentStyles.Quote,
            "Una cita breve ayuda a fijar la idea que debe recordar quien lea el informe.");
        return document;
    }

    public static FlowDocument Resume(PageSetup setup)
    {
        var document = DocumentStyles.CreateDocument(setup);
        Add(document, DocumentStyles.Title, "Nombre Apellido");
        Add(document, DocumentStyles.Subtitle, "Puesto o especialidad · ciudad · correo · teléfono");
        Add(document, DocumentStyles.Heading1, "Perfil");
        Add(document, DocumentStyles.Normal,
            "Dos o tres líneas sobre su experiencia, sus puntos fuertes y el tipo de trabajo que busca.");
        Add(document, DocumentStyles.Heading1, "Experiencia");
        Add(document, DocumentStyles.Heading2, "Puesto · Empresa · 2022 - actualidad");
        AddBullets(document,
            "Responsabilidad principal y equipo con el que trabajó.",
            "Un logro medible, con cifra si es posible.",
            "Herramientas o métodos que dominó en el puesto.");
        Add(document, DocumentStyles.Heading2, "Puesto · Empresa · 2019 - 2022");
        AddBullets(document,
            "Responsabilidad principal.",
            "Resultado del que se siente orgulloso.");
        Add(document, DocumentStyles.Heading1, "Formación");
        Add(document, DocumentStyles.Normal, "Titulación · Centro · Año");
        Add(document, DocumentStyles.Heading1, "Habilidades");
        AddBullets(document,
            "Idiomas y nivel.",
            "Herramientas técnicas.",
            "Competencias de organización o comunicación.");
        return document;
    }

    public static void Add(FlowDocument document, string key, string text) =>
        document.Blocks.Add(DocumentStyles.CreateParagraph(document, key, text));

    public static void AddBullets(FlowDocument document, params string[] items)
    {
        var list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new System.Windows.Thickness(0) };
        foreach (var item in items)
        {
            list.ListItems.Add(new ListItem(DocumentStyles.CreateParagraph(document, DocumentStyles.Normal, item)));
        }
        document.Blocks.Add(list);
    }
}
