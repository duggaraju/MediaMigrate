using System.Xml.Serialization;

namespace MediaMigrate.Contracts
{
    [XmlType(IncludeInSchema = false)]
    public enum StreamType
    {
        [XmlEnum("audio")]
        Audio,
        [XmlEnum("video")]
        Video,
        [XmlEnum("text")]
        Text
    }
}

