using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>One object that uses the object WhereUsed was asked about.</summary>
    public class ResponseUsage
    {
        public string? UsedBy { get; set; }

        public string? Path { get; set; }

        public string? Address { get; set; }

        public string? TypeName { get; set; }

        /// <summary>How the object is used there: Call, Read, Write, InstanceDB, Declaration, ...</summary>
        public string? Access { get; set; }

        /// <summary>Where exactly, as TIA Portal shows it, e.g. 'Main ▶ NW2 (Antrieb Band 1)'.</summary>
        public List<string> Locations { get; set; } = new List<string>();
    }
}
