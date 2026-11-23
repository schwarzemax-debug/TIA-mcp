using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// Completes hand-written (agent-written) SimaticML before it is imported.
    ///
    /// TIA Portal V20 refuses an FBD network in which a pin of a block call is missing from the
    /// wires - also the EN input and outputs nobody uses ("The connection with the name 'en' is
    /// not connected to the object with the UID '30'"). TIA's own export lists those pins with an
    /// &lt;OpenCon&gt;. Writing them by hand is pure boilerplate and the most common reason an
    /// otherwise correct FBD import fails, so they are added here.
    ///
    /// Scope, deliberately narrow: only &lt;Call&gt; elements (their pins are listed in CallInfo) in
    /// FBD networks. LAD networks connect EN to the power rail and are left as they are.
    /// Callers: Portal.WriteSourceText for format 'xml'. No file I/O.
    /// </summary>
    public static class SimaticMlNormalizer
    {
        /// <summary>FBD box instructions (SimaticML Part names) that have an EN input.</summary>
        private static readonly HashSet<string> EnBoxes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Move", "Add", "Sub", "Mul", "Div", "Mod", "Neg", "Abs", "Expt", "Sqr", "Sqrt", "Ln", "Exp",
            "Sin", "Cos", "Tan", "Asin", "Acos", "Atan", "Frac", "Inc", "Dec", "Calculate",
            "Convert", "Round", "Trunc", "Ceil", "Floor", "Scale_X", "Norm_X",
            "Min", "Max", "Limit", "Sel", "Mux", "Demux",
            "Shl", "Shr", "Rol", "Ror", "Swap", "And", "Or", "Xor", "Inv", "Invert", "Decode", "Encode",
            "Fill", "Fill_Blk", "UFill_Blk", "Move_Blk", "UMove_Blk", "MoveBlockVariant", "Serialize", "Deserialize",
            "S_Move", "S_Conv", "Concat", "Left", "Right", "Mid", "Len", "Find", "Delete", "Insert", "Replace",
            "Strg_Val", "Val_Strg", "Chars_To_Strg", "Strg_To_Chars",
            "T_Add", "T_Sub", "T_Diff", "T_Conv", "T_Combine"
        };

        /// <summary>Returns the completed document text; 'notes' says what was added, per network.</summary>
        public static string CompleteOpenPins(string xml, List<string> notes)
        {
            XDocument doc;

            try
            {
                doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            }
            catch (Exception ex)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"The SimaticML text is not well-formed XML: {ex.Message}", null, ex);
            }

            var changed = false;
            var networkIndex = 0;

            foreach (var compileUnit in doc.Descendants().Where(e => e.Name.LocalName == "SW.Blocks.CompileUnit"))
            {
                networkIndex++;

                var language = compileUnit.Element("AttributeList")?.Element("ProgrammingLanguage")?.Value ?? string.Empty;
                if (!string.Equals(language, "FBD", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var flgNet = compileUnit.Descendants().FirstOrDefault(e => e.Name.LocalName == "FlgNet");
                if (flgNet == null)
                {
                    continue;
                }

                var ns = flgNet.Name.Namespace;
                var parts = flgNet.Element(ns + "Parts");
                if (parts == null)
                {
                    continue;
                }

                var wires = flgNet.Element(ns + "Wires");
                if (wires == null)
                {
                    wires = new XElement(ns + "Wires");
                    flgNet.Add(wires);
                }

                var connected = new HashSet<string>(
                    wires.Elements(ns + "Wire")
                        .SelectMany(w => w.Elements(ns + "NameCon"))
                        .Select(n => $"{(string?)n.Attribute("UId")}|{(string?)n.Attribute("Name")}"),
                    StringComparer.OrdinalIgnoreCase);

                var nextUid = flgNet.Descendants()
                    .Select(e => int.TryParse((string?)e.Attribute("UId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0)
                    .DefaultIfEmpty(20)
                    .Max() + 1;

                // Boxes with an EN input (MOVE, ADD, CONVERT, ...): TIA wants the EN wired too. Bit
                // logic, comparators, flip-flops and IEC timers/counters have no EN in FBD and are
                // not touched - hence a positive list instead of "every part".
                foreach (var part in parts.Elements(ns + "Part"))
                {
                    var uid = (string?)part.Attribute("UId");
                    var partName = (string?)part.Attribute("Name") ?? string.Empty;

                    if (string.IsNullOrEmpty(uid) || !EnBoxes.Contains(partName) || connected.Contains($"{uid}|en"))
                    {
                        continue;
                    }

                    var open = new XElement(ns + "OpenCon", new XAttribute("UId", nextUid++));
                    var nameCon = new XElement(ns + "NameCon", new XAttribute("UId", uid), new XAttribute("Name", "en"));
                    wires.Add(new XElement(ns + "Wire", new XAttribute("UId", nextUid++), open, nameCon));
                    connected.Add($"{uid}|en");
                    changed = true;
                    notes.Add($"NW{networkIndex}: box '{partName}' (UId {uid}) - unconnected 'en' added as open connection (<OpenCon>).");
                }

                foreach (var call in parts.Elements(ns + "Call"))
                {
                    var uid = (string?)call.Attribute("UId");
                    if (string.IsNullOrEmpty(uid))
                    {
                        continue;
                    }

                    var callInfo = call.Element(ns + "CallInfo");
                    var callName = (string?)callInfo?.Attribute("Name") ?? uid;
                    var pins = new List<(string Name, bool IsOutput)> { ("en", false) };

                    foreach (var parameter in callInfo?.Elements(ns + "Parameter") ?? Enumerable.Empty<XElement>())
                    {
                        var name = (string?)parameter.Attribute("Name");
                        var section = (string?)parameter.Attribute("Section") ?? string.Empty;
                        var type = (string?)parameter.Attribute("Type") ?? string.Empty;

                        if (string.IsNullOrEmpty(name) || string.Equals(type, "Void", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var isOutput = section.Equals("Output", StringComparison.OrdinalIgnoreCase) || section.Equals("Return", StringComparison.OrdinalIgnoreCase);
                        pins.Add((name!, isOutput));
                    }

                    var added = new List<string>();

                    foreach (var pin in pins)
                    {
                        if (connected.Contains($"{uid}|{pin.Name}"))
                        {
                            continue;
                        }

                        var open = new XElement(ns + "OpenCon", new XAttribute("UId", nextUid++));
                        var nameCon = new XElement(ns + "NameCon", new XAttribute("UId", uid), new XAttribute("Name", pin.Name));
                        var wire = new XElement(ns + "Wire", new XAttribute("UId", nextUid++));

                        wire.Add(pin.IsOutput ? new object[] { nameCon, open } : new object[] { open, nameCon });
                        wires.Add(wire);

                        connected.Add($"{uid}|{pin.Name}");
                        added.Add(pin.Name);
                    }

                    if (added.Count > 0)
                    {
                        changed = true;
                        notes.Add($"NW{networkIndex}: call '{callName}' - unconnected pin(s) {string.Join(", ", added.Select(a => $"'{a}'"))} added as open connections (<OpenCon>).");
                    }
                }
            }

            if (!changed)
            {
                return xml;
            }

            var declaration = doc.Declaration?.ToString() ?? "<?xml version=\"1.0\" encoding=\"utf-8\"?>";

            return declaration + Environment.NewLine + doc.Root!.ToString(SaveOptions.DisableFormatting);
        }
    }
}
