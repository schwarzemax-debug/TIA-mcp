using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>Result of WriteBlockSource: what was generated from the source text and how it compiled.</summary>
    public class ResponseWrittenSource : ResponseMessage
    {
        /// <summary>Blocks and PLC data types generated (or replaced) from the source text.</summary>
        public List<ResponseImportedProgramItem> Items { get; set; } = new List<ResponseImportedProgramItem>();

        /// <summary>True when the generated blocks were compiled; false when compile was off or only types were generated.</summary>
        public bool Compiled { get; set; }

        /// <summary>Worst compile state over all generated blocks: Success, Information, Warning or Error. Null when not compiled.</summary>
        public string? State { get; set; }

        public int ErrorCount { get; set; }

        public int WarningCount { get; set; }

        /// <summary>Every compiler message with the object it belongs to. Read this to fix errors, then call WriteBlockSource again with the corrected text.</summary>
        public List<CompileMessage> Messages { get; set; } = new List<CompileMessage>();

        /// <summary>The compile result as markdown tables; show it to the user unchanged.</summary>
        public string? Report { get; set; }

        /// <summary>What the server completed in the text before the import (e.g. open pins of FBD calls).</summary>
        public List<string> Notes { get; set; } = new List<string>();
    }
}
