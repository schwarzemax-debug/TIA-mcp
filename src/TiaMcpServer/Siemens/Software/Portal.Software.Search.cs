using Microsoft.Extensions.Logging;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.Multiuser;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.WatchAndForceTables;
using Siemens.Engineering.Safety;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // From the former Portal.Search.cs:
        // Searching the program text, not just object names.
        //
        // Callers: the FindInCode tool in McpServer.Software.Search.cs. Affected API: none existing - every
        // member here is new.
        //
        // File I/O: like the source readers, this has to export before it can search, because
        // Openness offers no in-memory access to a block body. Everything is written to a scratch
        // directory under the temp path and removed again.
        //
        // Cost: one export per candidate object, per call. A whole PLC of this project's size
        // exports in roughly ten seconds, so no persistent cache is kept - invalidating one
        // correctly is not worth the complexity at that scale. Narrow the work with 'nameFilter'
        // on a large PLC.

        #region search

        /// <summary>
        /// Finds a regular expression in the source text of blocks and PLC data types.
        /// </summary>
        /// <param name="pattern">Regular expression matched against each line, case-insensitive.</param>
        /// <param name="nameFilter">Optional regex on object names, to limit what gets exported.</param>
        /// <param name="maxResults">Stops once this many matches are found.</param>
        public CodeSearchResult FindInCode(
            string softwarePath,
            string pattern,
            string nameFilter = "",
            int maxResults = 200)
        {
            return Operation.Run(_logger, nameof(FindInCode), PortalErrorCode.ExportFailed,
                () =>
                {
                    if (string.IsNullOrWhiteSpace(pattern))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            "'pattern' is empty. Pass the text or regular expression to search for.");
                    }

                    if (maxResults <= 0)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "'maxResults' must be greater than zero.");
                    }

                    Regex regex;

                    try
                    {
                        regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    }
                    catch (ArgumentException ex)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"'{pattern}' is not a valid regular expression: {ex.Message}", null, ex);
                    }

                    var result = new CodeSearchResult { Pattern = pattern };

                    // Blocks and types are read through the same readers the source tools use, so
                    // a hit always corresponds to text the caller can fetch with GetDocumentsBlock.
                    var targets = new List<(string Path, bool IsBlock)>();

                    // Inconsistent objects cannot be exported. They used to be dropped silently, so a
                    // search could report "0 matches" for a tag that IS used - name them instead.
                    foreach (var block in GetBlocks(softwarePath, nameFilter))
                    {
                        if (block.IsConsistent)
                        {
                            targets.Add((GetBlockPath(block), true));
                        }
                        else
                        {
                            result.Unsearchable.Add($"{GetBlockPath(block)}: inconsistent, TIA Portal cannot export it - compile first ('CompilePlcSoftware').");
                        }
                    }

                    foreach (var type in GetTypes(softwarePath, nameFilter))
                    {
                        if (type.IsConsistent)
                        {
                            targets.Add((GetTypePath(type), false));
                        }
                        else
                        {
                            result.Unsearchable.Add($"{GetTypePath(type)}: inconsistent, TIA Portal cannot export it - compile first ('CompilePlcSoftware').");
                        }
                    }

                    result.ObjectsSearched = targets.Count;

                    foreach (var target in targets)
                    {
                        if (result.Items.Count >= maxResults)
                        {
                            result.Truncated = true;

                            break;
                        }

                        SourceTextResult source;

                        try
                        {
                            source = target.IsBlock
                                ? ReadBlockForSearch(softwarePath, target.Path)
                                : ReadTypeForSearch(softwarePath, target.Path);
                        }
                        catch (Exception ex)
                        {
                            // One unreadable object must not end the search; know-how protection
                            // and export refusals are both normal on a real PLC.
                            result.Unsearchable.Add($"{target.Path}: {ex.Message}");
                            _logger?.LogDebug(ex, "Skipping '{Path}' during code search", target.Path);

                            continue;
                        }

                        AddMatches(result, target.Path, source, regex, maxResults);
                    }

                    _logger?.LogInformation(
                        "FindInCode '{Pattern}' searched {Count} object(s) in '{Software}': {Hits} hit(s), {Skipped} unsearchable",
                        pattern, result.ObjectsSearched, softwarePath, result.Items.Count, result.Unsearchable.Count);

                    return result;
                },
                ("softwarePath", softwarePath), ("pattern", pattern), ("nameFilter", nameFilter));
        }

        /// <summary>
        /// Documents are the readable form, but STL and mixed-language blocks have none; a search
        /// should still cover them, so those fall back to XML here (and only here).
        /// </summary>
        private SourceTextResult ReadBlockForSearch(string softwarePath, string blockPath)
        {
            try
            {
                return GetDocumentsBlock(softwarePath, blockPath, int.MaxValue);
            }
            catch (PortalException pex) when (pex.Code == PortalErrorCode.NotSupported || pex.Code == PortalErrorCode.InvalidState || pex.Code == PortalErrorCode.ExportFailed)
            {
                _logger?.LogDebug(pex, "No source document for '{Path}', searching its source/XML instead", blockPath);

                // SCL/STL/DB: the readable source text; everything else (LAD, FBD, GRAPH): SimaticML.
                try
                {
                    return GetSourceBlock(softwarePath, blockPath, false, int.MaxValue);
                }
                catch (PortalException)
                {
                    return GetXmlBlock(softwarePath, blockPath, int.MaxValue);
                }
            }
        }

        /// <summary>
        /// PLC data types: documents need TIA Portal V21 (V20 throws InvalidState, which the old
        /// fallback did not catch). The '.udt' source works in every version, XML is the last resort.
        /// </summary>
        private SourceTextResult ReadTypeForSearch(string softwarePath, string typePath)
        {
            if (Engineering.TiaMajorVersion >= 21)
            {
                try
                {
                    return GetDocumentsType(softwarePath, typePath, int.MaxValue);
                }
                catch (PortalException pex)
                {
                    _logger?.LogDebug(pex, "No source document for '{Path}', searching its source instead", typePath);
                }
            }

            try
            {
                return GetSourceType(softwarePath, typePath, false, int.MaxValue);
            }
            catch (PortalException pex)
            {
                _logger?.LogDebug(pex, "No source for '{Path}', searching its XML instead", typePath);

                return GetXmlType(softwarePath, typePath, int.MaxValue);
            }
        }

        private static void AddMatches(CodeSearchResult result, string objectPath, SourceTextResult source, Regex regex, int maxResults)
        {
            var lines = source.Text.Split('\n');
            var isXml = string.Equals(source.Format, "xml", StringComparison.OrdinalIgnoreCase);
            var network = 0;

            for (var i = 0; i < lines.Length; i++)
            {
                if (result.Items.Count >= maxResults)
                {
                    result.Truncated = true;

                    return;
                }

                // SimaticML: a line number is meaningless to a person - track the network
                // (CompileUnit) a line belongs to, numbered as TIA Portal shows it.
                if (isXml && lines[i].Contains("<SW.Blocks.CompileUnit "))
                {
                    network++;
                }

                if (!regex.IsMatch(lines[i]))
                {
                    continue;
                }

                result.Items.Add(new CodeMatch
                {
                    ObjectPath = objectPath,
                    Format = source.Format,
                    Line = i + 1,
                    Network = isXml && network > 0 ? $"NW{network}" : null,
                    Text = lines[i].Trim('\r').Trim()
                });
            }
        }

        #endregion
    }

    /// <summary>One line of source text that matched the search.</summary>
    public class CodeMatch
    {
        /// <summary>Root-relative path, ready to pass to the GetXml*, GetSource* and GetDocuments* tools.</summary>
        public string ObjectPath { get; set; } = string.Empty;

        /// <summary>'document' or 'xml' - which representation was searched.</summary>
        public string Format { get; set; } = string.Empty;

        /// <summary>1-based line number within that representation.</summary>
        public int Line { get; set; }

        /// <summary>For LAD/FBD/GRAPH blocks (searched as SimaticML): the network, e.g. 'NW2'.</summary>
        public string? Network { get; set; }

        public string Text { get; set; } = string.Empty;
    }

    /// <summary>Outcome of a code search.</summary>
    public class CodeSearchResult
    {
        public string Pattern { get; set; } = string.Empty;

        public List<CodeMatch> Items { get; set; } = new List<CodeMatch>();

        /// <summary>How many objects were exported and scanned.</summary>
        public int ObjectsSearched { get; set; }

        /// <summary>Objects that could not be read, with the reason.</summary>
        public List<string> Unsearchable { get; set; } = new List<string>();

        /// <summary>True when maxResults cut the search short, so the list is incomplete.</summary>
        public bool Truncated { get; set; }
    }
}
