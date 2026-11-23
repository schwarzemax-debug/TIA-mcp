using System;
using System.Collections.Generic;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// Turns an exception thrown by an Openness import into one message for the caller.
    ///
    /// TIA Portal names the exact problem in the exception (and often in an inner exception), for
    /// example which part of a network is out of order. The import tools used to swallow it and
    /// only report "failed". Callers: ImportXmlBlock and ImportXmlType in Portal. Reads/writes
    /// no data files.
    /// </summary>
    public static class ImportErrors
    {
        private const string FlowOrderMarker = "must be sorted according to the current flow";

        private const string FlowOrderHint =
            "TIA Portal needs the elements of a network in flow order: put each new <Part> directly " +
            "after the part that feeds it, the order TIA Portal's own export uses.";

        private const string NotConnectedMarker = "is not connected to the object with the UID";

        private const string NotConnectedHint =
            "In LAD/FBD SimaticML every pin of a box or call must appear in a <Wire>, also EN and unused outputs: " +
            "connect an unused pin with <OpenCon UId=\"..\" /> (e.g. <Wire UId=\"..\"><OpenCon UId=\"..\" /><NameCon UId=\"30\" Name=\"en\" /></Wire>). " +
            "'WriteBlockSource' (format 'xml') adds these automatically for <Call> elements in FBD networks.";

        /// <summary>
        /// The distinct messages of the exception and its inner exceptions, outermost first,
        /// joined with ': '. A hint is appended for failures that have a known remedy.
        /// </summary>
        public static string Describe(Exception exception)
        {
            var messages = new List<string>();

            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                var message = current.Message?.Trim();

                if (!string.IsNullOrEmpty(message) && !messages.Contains(message!))
                {
                    messages.Add(message!);
                }
            }

            var text = string.Join(": ", messages);

            if (text.IndexOf(FlowOrderMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                text += " " + FlowOrderHint;
            }

            if (text.IndexOf(NotConnectedMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                text += " " + NotConnectedHint;
            }

            return text;
        }
    }
}
