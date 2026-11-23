using Microsoft.Extensions.Logging;
using Siemens.Engineering.SW.ExternalSources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Writes source text (SCL, STL, DB or UDT) straight into the project as compiled blocks or
        // PLC data types - no export folder, no git, no file the caller has to place first.
        //
        // Callers: the WriteBlockSource tool in McpServer.WriteSource.cs. Affected API: none
        // existing - this only composes ImportOneSource (Portal.Sources.cs), which does the
        // Openness work (scratch external source -> GenerateBlocksFromSource -> delete scratch).
        // File I/O: one temp file in a fresh temp directory, removed again in every case.
        //
        // The object name comes from the source text itself (FUNCTION_BLOCK "FB_Name", TYPE "UDT_Name"),
        // not from a parameter. A missing target group is created, segment by segment.

        #region write source text

        /// <summary>Source formats WriteSourceText accepts, mapped to whether the result is a PLC data type.</summary>
        public static readonly IReadOnlyDictionary<string, bool> WritableSourceFormats =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["scl"] = false,
                ["awl"] = false,
                ["db"] = false,
                ["udt"] = true,
                ["xml"] = false // SimaticML: LAD, FBD (and every other language) as the XML 'GetXmlBlock' returns
            };

        /// <summary>
        /// Generates blocks or PLC data types from source text and returns what was created or
        /// replaced. Objects of the same name in the project are replaced.
        /// </summary>
        /// <param name="groupPath">Root-relative user group below 'Program blocks' (or 'PLC data types' for udt); created when missing; empty uses the default location of the source.</param>
        /// <param name="sourceText">The complete source file content.</param>
        /// <param name="format">scl, awl, db or udt.</param>
        /// <param name="keepOnError">Keep the objects that were generated even when others in the same text fail.</param>
        public List<ImportedSource> WriteSourceText(string softwarePath, string groupPath, string sourceText, string format, bool keepOnError, List<string>? notes = null)
        {
            return Operation.Run(_logger, nameof(WriteSourceText), PortalErrorCode.ImportFailed,
                () =>
                {
                    if (string.IsNullOrWhiteSpace(sourceText))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "sourceText is empty.");
                    }

                    if (!WritableSourceFormats.TryGetValue(format ?? string.Empty, out var isType))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"Unknown format '{format}'. Use one of: {string.Join(", ", WritableSourceFormats.Keys)}.");
                    }

                    var normalizedGroup = NormalizeGroupPath(groupPath);

                    if (normalizedGroup.Length > 0)
                    {
                        EnsureGroupPath(softwarePath, normalizedGroup, isType);
                    }

                    var directory = Path.Combine(Path.GetTempPath(), "TiaMcpWrite_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(directory);

                    try
                    {
                        var file = Path.Combine(directory, "Source." + format!.ToLowerInvariant());

                        if (format.Equals("xml", StringComparison.OrdinalIgnoreCase))
                        {
                            // FBD networks: pins of block calls that the text leaves out (EN, unused
                            // outputs) are added as open connections - TIA refuses the import otherwise.
                            var completed = SimaticMlNormalizer.CompleteOpenPins(sourceText, notes ?? new List<string>());

                            // The file carries its own XML declaration, so no BOM.
                            File.WriteAllText(file, completed, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                            return ImportBlocksFromXmlFile(softwarePath, normalizedGroup, file)
                                .Select(b => new ImportedSource { Name = b.Name, Path = GetBlockPath(b), Kind = "block" })
                                .ToList();
                        }

                        File.WriteAllText(file, sourceText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

                        var option = keepOnError ? GenerateBlockOption.KeepOnError : GenerateBlockOption.None;

                        return ImportOneSource(softwarePath, file, normalizedGroup, option, isType);
                    }
                    finally
                    {
                        try
                        {
                            Directory.Delete(directory, recursive: true);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Could not remove temp directory '{Directory}'", directory);
                        }
                    }
                },
                ("softwarePath", softwarePath), ("groupPath", groupPath), ("format", format ?? string.Empty));
        }

        /// <summary>Creates every missing segment of a block or type group path.</summary>
        private void EnsureGroupPath(string softwarePath, string normalizedGroupPath, bool isType)
        {
            var segments = normalizedGroupPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var parent = string.Empty;

            foreach (var segment in segments)
            {
                var current = parent.Length == 0 ? segment : parent + "/" + segment;
                var exists = isType
                    ? GetPlcTypeGroupByPath(softwarePath, current) != null
                    : GetPlcBlockGroupByPath(softwarePath, current) != null;

                if (!exists)
                {
                    if (isType)
                    {
                        CreateTypeGroup(softwarePath, parent, segment);
                    }
                    else
                    {
                        CreateBlockGroup(softwarePath, parent, segment);
                    }
                }

                parent = current;
            }
        }

        #endregion
    }
}
