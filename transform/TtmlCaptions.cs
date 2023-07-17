using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text;
using System.Xml.Serialization;

namespace MediaMigrate.Transform
{
    class Constants
    {
        public const string TtmlNamespace = "http://www.w3.org/ns/ttml";
        public const string TtmlStylingNamespace = "http://www.w3.org/ns/ttml#styling";
        public const string TtmlDatanamespace = "http://www.w3.org/ns/ttml#Data";
    }

    [XmlType(Namespace = Constants.TtmlNamespace)]
    public class Caption
    {
        [XmlIgnore]
        public TimeSpan Start { get; set; }

        [XmlAttribute("begin")]
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public string StartString
        {
            get => Start.ToString(); set => Start = TimeSpan.Parse(value);
        }

        [XmlIgnore]
        public TimeSpan End { get; set; }

        [XmlAttribute("end")]
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public string EndString
        {
            get => End.ToString(); set => End = TimeSpan.Parse(value);
        }

        [XmlText]
        public string Text { get; set; } = string.Empty;

        [XmlAttribute("textAlign", Namespace = Constants.TtmlStylingNamespace)]
        public string Alignment { get; set; } = "center";

        [XmlAttribute("extent", Namespace = Constants.TtmlStylingNamespace)]
        public string Extent { get; set; } = string.Empty;

        [XmlAttribute("origin", Namespace = Constants.TtmlStylingNamespace)]
        public string Origin { get; set; } = string.Empty;
    }

    [XmlType(Namespace = Constants.TtmlNamespace)]
    public class TtmlHead
    {
    }

    [XmlType(Namespace = Constants.TtmlNamespace)]
    public class TtmlBody
    {
        [XmlAttribute("region")]
        public string Region { get; set; } = string.Empty;

        [XmlArray("div")]
        [XmlArrayItem("p")]
        public List<Caption> Captions { get; set; } = new List<Caption>();
    }

    [XmlRoot("tt", Namespace = Constants.TtmlNamespace)]
    public class TtmlCaptions
    {
        [XmlAttribute("xml:lang")]
        public string Language { get; set; } = string.Empty;

        [XmlElement("head", Namespace = Constants.TtmlNamespace)]
        public TtmlHead Head { get; set; } = new TtmlHead();

        [XmlElement("body", Namespace = Constants.TtmlNamespace)]
        public TtmlBody Body { get; set; } = new TtmlBody();

        public static TtmlCaptions Parse(Stream stream, ILogger logger)
        {
            var serializer = new XmlSerializer(typeof(TtmlCaptions));
            serializer.UnknownElement += (s, args) =>
            {
                logger.LogTrace("Unknown element in manifest {element}", args.Element);
            };

            serializer.UnknownAttribute += (s, args) =>
            {
                logger.LogTrace("Unknown attribute in manifest {element}", args.Attr);
            };
            var reader = new StreamReader(stream, Encoding.UTF8);
            var captions = serializer.Deserialize(reader) as TtmlCaptions;
            return captions ?? throw new InvalidOperationException("Captions are malformed!!");
        }
    }
}
