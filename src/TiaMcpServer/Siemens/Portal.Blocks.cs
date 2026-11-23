using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.Multiuser;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Blocks.Interface;
using Siemens.Engineering.SW.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        #region blocks

        public PlcBlock? GetBlock(string softwarePath, string blockPath)
        {
            _logger?.LogInformation($"Getting block by path: {blockPath}");

            if (IsProjectNull())
            {
                return null;
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software is PlcSoftware plcSoftware)
            {
                var blockGroup = plcSoftware?.BlockGroup;

                if (blockGroup is not null)
                {
                    var path = blockPath.Contains("/") ? blockPath.Substring(0, blockPath.LastIndexOf("/")) : string.Empty;
                    var regexName = blockPath.Contains("/") ? blockPath.Substring(blockPath.LastIndexOf("/") + 1) : blockPath;

                    PlcBlock? block = null;

                    var group = GetPlcBlockGroupByPath(softwarePath, path);
                    if (group is not null)
                    {
                        if (regexName.IndexOfAny(_regexChars) >= 0)
                        {
                            try
                            {
                                var regex = new Regex(regexName, RegexOptions.IgnoreCase);
                                block = group.Blocks.FirstOrDefault(b => regex.IsMatch(b.Name)) as PlcBlock;
                            }
                            catch (Exception)
                            {
                                // Invalid regex, return null
                                return null;
                            }
                        }
                        else
                        {
                            block = group.Blocks.FirstOrDefault(b => b.Name.Equals(regexName, StringComparison.OrdinalIgnoreCase));
                        }

                        return block;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Root-relative path of a block, e.g. "Common/CarrierRegister/GLOBAL_POSITIONING".
        /// The system group ("Program blocks") is deliberately excluded so the result can be fed
        /// straight back into GetBlock/ExportXmlBlock as a blockPath.
        /// </summary>
        public string GetBlockPath(PlcBlock block)
        {
            if (block == null)
            {
                return string.Empty;
            }

            if (block.Parent is PlcBlockGroup parentGroup)
            {
                var groupPath = GetPlcBlockGroupPath(parentGroup, includeSystemRoot: false);
                return string.IsNullOrEmpty(groupPath) ? block.Name : $"{groupPath}/{block.Name}";
            }

            return block.Name;
        }

        public List<PlcBlock> GetBlocks(string softwarePath, string regexName = "")
        {
            _logger?.LogInformation("Getting blocks...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<PlcBlock>();

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is PlcSoftware plcSoftware)
                {
                    var group = plcSoftware?.BlockGroup;

                    if (group != null)
                    {
                        GetBlocksRecursive(group, list, regexName);
                    }
                }
            }
            catch (Exception)
            {
                // Console.WriteLine($"Error getting blocks: {ex.Message}");
            }

            return list;
        }

        public PlcBlockGroup? GetBlockRootGroup(string softwarePath)
        {
            _logger?.LogInformation("Getting block root group...");

            if (IsProjectNull())
            {
                return null;
            }

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is PlcSoftware plcSoftware)
                {
                    return plcSoftware.BlockGroup;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting block root group");
            }

            return null;
        }

        public PlcBlock? ExportXmlBlock(string softwarePath, string blockPath, string exportPath, bool keepStructure = false)
        {
            _logger?.LogInformation($"Exporting block by path: {blockPath}");

            try
            {
                if (IsProjectNull())
                {
                    throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
                }

                var block = GetBlock(softwarePath, blockPath);

                if (block == null)
                {
                    throw new PortalException(PortalErrorCode.NotFound, "Block not found");
                }

                exportPath = ResolveSoftwareRoot(exportPath, softwarePath);

                if (keepStructure)
                {
                    var groupPath = "";
                    if (block.Parent is PlcBlockGroup parentGroup)
                    {
                        groupPath = GetPlcBlockGroupPath(parentGroup);
                    }

                    exportPath = Path.Combine(exportPath, groupPath.Replace('/', '\\'), $"{FileNames.Safe(block.Name)}.xml");
                }
                else
                {
                    exportPath = Path.Combine(exportPath, $"{FileNames.Safe(block.Name)}.xml");
                }

                // TIA Portal never exports inconsistent blocks
                if (!block.IsConsistent)
                {
                    throw new PortalException(PortalErrorCode.InvalidState, "Block is inconsistent; TIA Portal does not export inconsistent blocks.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);

                if (File.Exists(exportPath))
                {
                    File.Delete(exportPath);
                }

                block.Export(new FileInfo(exportPath), ExportOptions.None);

                return block;
            }
            catch (Exception ex)
            {
                //If the exception is already a PortalException, use it; otherwise, wrap it in a new PortalException
                var pex = ex as PortalException ?? new PortalException(PortalErrorCode.ExportFailed, "Export failed", null, ex);

                pex.Data["softwarePath"] = softwarePath;
                pex.Data["blockPath"] = blockPath;
                pex.Data["exportPath"] = exportPath;

                _logger?.LogError(pex, "ExportXmlBlock failed for {SoftwarePath} {BlockPath} -> {ExportPath}", softwarePath, blockPath, exportPath);
                throw pex;
            }
        }

        /// <summary>
        /// Imports one SimaticML file into a block group, replacing blocks of the same name, and
        /// returns the blocks TIA Portal created. Shared by ImportXmlBlock and OverwritePlcProgram.
        /// Throws a PortalException with TIA Portal's own reason when nothing could be imported.
        /// </summary>
        private IList<PlcBlock> ImportBlocksFromXmlFile(string softwarePath, string groupPath, string importPath)
        {
            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState,
                    "No project is open. Open one with 'OpenProject' first.");
            }

            var group = GetPlcBlockGroupByPath(softwarePath, groupPath)
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"Block group not found at '{groupPath}'.");

            if (!File.Exists(importPath))
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"Import file '{importPath}' does not exist on the machine running this server.");
            }

            try
            {
                var imported = group.Blocks.Import(new FileInfo(importPath), ImportOptions.Override);

                if (imported == null || imported.Count == 0)
                {
                    throw new PortalException(PortalErrorCode.ImportFailed,
                        $"TIA Portal imported no block from '{importPath}'.");
                }

                return imported;
            }
            catch (Exception ex) when (!(ex is PortalException))
            {
                // Keep TIA Portal's own reason: it usually names the exact problem.
                throw new PortalException(PortalErrorCode.ImportFailed,
                    $"Import of block file '{importPath}' failed: {ImportErrors.Describe(ex)}", null, ex);
            }
        }

        #endregion

        #region get...by path

        private PlcBlockGroup? GetPlcBlockGroupByPath(string softwarePath, string groupPath)
        {
            if (_project == null)
            {
                return null;
            }

            var plcSoftware = (GetSoftwareContainer(softwarePath)?.Software as PlcSoftware);

            return plcSoftware?.BlockGroup == null
                ? null
                : WalkGroups<PlcBlockGroup>(plcSoftware.BlockGroup, groupPath, BlockSubgroups, g => g.Name);
        }

        /// <param name="includeSystemRoot">
        /// True (the default) prefixes the system group name, e.g. "Program blocks/1_Tests".
        /// That is the layout the keepStructure exports write and Test_415_ImportBlock reads back.
        /// False yields "1_Tests", which round-trips into GetPlcBlockGroupByPath.
        /// </param>
        private string GetPlcBlockGroupPath(PlcBlockGroup group, bool includeSystemRoot = true)
        {
            return BuildGroupPath<PlcBlockGroup>(
                group,
                g => g.Parent as PlcBlockGroup,
                g => g.Name,
                g => g is PlcBlockSystemGroup,
                includeSystemRoot);
        }

        #endregion

        #region get recursive ...

        private bool GetBlocksRecursive(PlcBlockGroup group, List<PlcBlock> list, string regexName = "")
        {
            var before = list.Count;

            WalkRecursive<PlcBlockGroup, PlcBlock>(group, list, g => g.Blocks, BlockSubgroups, b => b.Name, regexName);

            // The previous implementation reported only the last subgroup's outcome; report
            // whether anything at all was collected.
            return list.Count > before;
        }

        #endregion

        // From the former Portal.BlockCrud.cs:
        // Create, rename and delete for program blocks, PLC data types and their groups.
        // The type half of this set (type groups, DeleteType, RenameType) lives in Portal.Types.cs.
        //
        // Callers: the corresponding tools in McpServer.Blocks.cs and McpServer.Types.cs, which are only registered when the
        // server runs with '--allow-write'. Affected API: none existing - all members are new.
        // Reads/writes no data files; these mutate the open project in memory until SaveProject.
        //
        // Openness constraints that shape this file:
        // - PlcBlockComposition has no generic Create(name) and no CreateFrom(PlcBlock), so the
        // only ways to make a block are CreateFB, CreateInstanceDB and XML import.
        // - Only user groups can be created or deleted; the system roots cannot.
        // - Name has a public setter on blocks, types and user groups, so rename is assignment.

        #region guards

        /// <summary>
        /// Know-how protected objects reject edits deep inside Openness with an opaque error;
        /// failing here gives the caller something actionable instead.
        /// </summary>
        private static void EnsureNotKnowHowProtected(PlcBlock block)
        {
            if (block.IsKnowHowProtected)
            {
                throw new PortalException(PortalErrorCode.InvalidState,
                    $"Block '{block.Name}' is know-how protected. Remove the protection in TIA Portal before modifying it.");
            }
        }

        private static void EnsureNotKnowHowProtected(PlcType type)
        {
            if (type.IsKnowHowProtected)
            {
                throw new PortalException(PortalErrorCode.InvalidState,
                    $"Type '{type.Name}' is know-how protected. Remove the protection in TIA Portal before modifying it.");
            }
        }

        private static PlcBlockUserGroup EnsureUserGroup(PlcBlockGroup? group, string groupPath)
        {
            if (group == null)
            {
                throw new PortalException(PortalErrorCode.NotFound,
                    $"Block group not found at '{groupPath}'. Use 'GetSoftwareTree' to discover valid group paths.");
            }

            return group as PlcBlockUserGroup
                ?? throw new PortalException(PortalErrorCode.NotSupported,
                    $"'{groupPath}' is the system group 'Program blocks', which cannot be created, renamed or deleted.");
        }

        private static PlcTypeUserGroup EnsureUserGroup(PlcTypeGroup? group, string groupPath)
        {
            if (group == null)
            {
                throw new PortalException(PortalErrorCode.NotFound,
                    $"Type group not found at '{groupPath}'. Use 'GetSoftwareTree' to discover valid group paths.");
            }

            return group as PlcTypeUserGroup
                ?? throw new PortalException(PortalErrorCode.NotSupported,
                    $"'{groupPath}' is the system group 'PLC data types', which cannot be created, renamed or deleted.");
        }

        private static void EnsureValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new PortalException(PortalErrorCode.InvalidParams, "The name must not be empty.");
            }

            if (name.Contains("/"))
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"'{name}' must be a plain name, not a path: '/' separates path segments.");
            }
        }

        #endregion

        #region groups

        /// <param name="parentGroupPath">Empty creates the group directly under the system root.</param>
        public PlcBlockUserGroup CreateBlockGroup(string softwarePath, string parentGroupPath, string name)
        {
            return Operation.Run(_logger, nameof(CreateBlockGroup), PortalErrorCode.CreateFailed,
                () =>
                {
                    EnsureValidName(name);

                    var parent = GetPlcBlockGroupByPath(softwarePath, parentGroupPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Block group not found at '{parentGroupPath}'.");

                    return parent.Groups.Create(name);
                },
                ("softwarePath", softwarePath), ("parentGroupPath", parentGroupPath), ("name", name));
        }

        public bool DeleteBlockGroup(string softwarePath, string groupPath)
        {
            return Operation.Run(_logger, nameof(DeleteBlockGroup), PortalErrorCode.DeleteFailed,
                () =>
                {
                    EnsureUserGroup(GetPlcBlockGroupByPath(softwarePath, groupPath), groupPath).Delete();
                    return true;
                },
                ("softwarePath", softwarePath), ("groupPath", groupPath));
        }

        #endregion

        #region blocks (create, rename, delete)

        public bool DeleteBlock(string softwarePath, string blockPath)
        {
            return Operation.Run(_logger, nameof(DeleteBlock), PortalErrorCode.DeleteFailed,
                () =>
                {
                    var block = GetBlock(softwarePath, blockPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Block not found at '{blockPath}'. Use 'GetBlocks' to list the available blocks.");

                    EnsureNotKnowHowProtected(block);
                    block.Delete();

                    return true;
                },
                ("softwarePath", softwarePath), ("blockPath", blockPath));
        }

        public bool RenameBlock(string softwarePath, string blockPath, string newName)
        {
            return Operation.Run(_logger, nameof(RenameBlock), PortalErrorCode.RenameFailed,
                () =>
                {
                    EnsureValidName(newName);

                    var block = GetBlock(softwarePath, blockPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Block not found at '{blockPath}'. Use 'GetBlocks' to list the available blocks.");

                    EnsureNotKnowHowProtected(block);
                    block.Name = newName;

                    return true;
                },
                ("softwarePath", softwarePath), ("blockPath", blockPath), ("newName", newName));
        }

        /// <summary>
        /// Creates an empty function block. Openness offers no generic "create block", so FB and
        /// instance DB are the only two kinds creatable without importing XML.
        /// </summary>
        public FB CreateFB(string softwarePath, string groupPath, string name, bool autoNumber = true, int number = 0, string language = "LAD")
        {
            return Operation.Run(_logger, nameof(CreateFB), PortalErrorCode.CreateFailed,
                () =>
                {
                    EnsureValidName(name);

                    if (!Enum.TryParse<ProgrammingLanguage>(language, true, out var parsedLanguage))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"Unknown programming language '{language}'. Allowed values are: {string.Join(", ", Enum.GetNames(typeof(ProgrammingLanguage)))}.");
                    }

                    EnsureBlockGroupExists(softwarePath, groupPath);
                    var group = GetPlcBlockGroupByPath(softwarePath, groupPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Block group not found at '{groupPath}'.");

                    if (BlockOrTypeNameExists(softwarePath, name))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"A block or PLC data type named '{name}' already exists in '{softwarePath}'. Names are unique per CPU.");
                    }

                    // V20 Openness creates FBs only in ProDiag ("The action 'Create block' only supports
                    // the programming language 'ProDiag'"). Every other language is created the way the
                    // TIA Portal UI would: SCL/STL from an empty source, LAD/FBD from an empty SimaticML block.
                    var effectiveNumber = autoNumber ? NextFreeBlockNumber(softwarePath, "FB") : number;

                    // Not "try the API, catch, fall back": write tools run inside a TIA transaction,
                    // and any Openness exception poisons it ("Commit of a Transaction is not allowed
                    // after an exception is thrown"). Decide up front instead.
                    if (parsedLanguage == ProgrammingLanguage.ProDiag || Engineering.TiaMajorVersion >= 21)
                    {
                        return group.Blocks.CreateFB(name, autoNumber, effectiveNumber, parsedLanguage);
                    }

                    return CreateEmptyCodeBlock(softwarePath, groupPath, group, "FB", name, parsedLanguage, effectiveNumber) as FB
                        ?? throw new PortalException(PortalErrorCode.CreateFailed, $"FB '{name}' was created but could not be found afterwards.");
                },
                ("softwarePath", softwarePath), ("groupPath", groupPath), ("name", name), ("language", language));
        }

        /// <param name="instanceOfName">Name of the FB this instance DB is created for.</param>
        public InstanceDB CreateInstanceDB(string softwarePath, string groupPath, string name, string instanceOfName, bool autoNumber = true, int number = 0)
        {
            return Operation.Run(_logger, nameof(CreateInstanceDB), PortalErrorCode.CreateFailed,
                () =>
                {
                    EnsureValidName(name);

                    if (string.IsNullOrWhiteSpace(instanceOfName))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            "instanceOfName must name the function block this instance DB belongs to.");
                    }

                    EnsureBlockGroupExists(softwarePath, groupPath);
                    var group = GetPlcBlockGroupByPath(softwarePath, groupPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Block group not found at '{groupPath}'.");

                    if (BlockOrTypeNameExists(softwarePath, name))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"A block or PLC data type named '{name}' already exists in '{softwarePath}'. Names are unique per CPU.");
                    }

                    // V20 takes 'number' literally even with isAutoNumbered=true: number 0 produced
                    // an invalid 'DB0' that only failed at compile time. Pass the first free number instead.
                    var effectiveNumber = autoNumber ? NextFreeBlockNumber(softwarePath, "DB") : number;

                    if (!autoNumber && effectiveNumber <= 0)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "'number' must be 1 or greater when autoNumber is false.");
                    }

                    return group.Blocks.CreateInstanceDB(name, autoNumber, effectiveNumber, instanceOfName);
                },
                ("softwarePath", softwarePath), ("groupPath", groupPath), ("name", name), ("instanceOfName", instanceOfName));
        }

        #endregion

        #region numbering and empty blocks

        /// <summary>Block number category: OB, FB, FC or DB (global, instance and array DBs share one range).</summary>
        private static string BlockNumberCategory(PlcBlock block)
        {
            switch (block)
            {
                case OB _: return "OB";
                case FB _: return "FB";
                case FC _: return "FC";
                default: return "DB";
            }
        }

        /// <summary>Smallest unused number of a category; OBs start at 123 (the range free for user OBs).</summary>
        public int NextFreeBlockNumber(string softwarePath, string category)
        {
            var used = new HashSet<int>(GetBlocks(softwarePath, string.Empty)
                .Where(b => string.Equals(BlockNumberCategory(b), category, StringComparison.OrdinalIgnoreCase))
                .Select(b => b.Number));

            var candidate = string.Equals(category, "OB", StringComparison.OrdinalIgnoreCase) ? 123 : 1;

            while (used.Contains(candidate))
            {
                candidate++;
            }

            return candidate;
        }

        /// <summary>Creates every missing segment of a block group path (like WriteBlockSource does).</summary>
        private void EnsureBlockGroupExists(string softwarePath, string groupPath)
        {
            var normalized = NormalizeGroupPath(groupPath);

            if (normalized.Length > 0)
            {
                EnsureGroupPath(softwarePath, normalized, false);
            }
        }

        /// <summary>Block and PLC data type names share one namespace per CPU.</summary>
        public bool BlockOrTypeNameExists(string softwarePath, string name)
        {
            return GetBlocks(softwarePath, string.Empty).Any(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))
                   || GetTypes(softwarePath, string.Empty).Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>First free '&lt;name&gt;_1', '&lt;name&gt;_2', ... - the same scheme TIA Portal uses for pasted copies.</summary>
        private string UniqueCopyName(string softwarePath, string name)
        {
            for (var i = 1; i < 10000; i++)
            {
                var candidate = $"{name}_{i}";

                if (!BlockOrTypeNameExists(softwarePath, candidate))
                {
                    return candidate;
                }
            }

            throw new PortalException(PortalErrorCode.InvalidState, $"No free copy name for '{name}'.");
        }

        /// <summary>
        /// Creates an empty FB/FC in any language: SCL and STL from a minimal source, LAD and FBD from a
        /// minimal SimaticML document with one empty network.
        /// </summary>
        private PlcBlock? CreateEmptyCodeBlock(string softwarePath, string groupPath, PlcBlockGroup group, string kind, string name, ProgrammingLanguage language, int number)
        {
            var keyword = kind == "FB" ? "FUNCTION_BLOCK" : "FUNCTION";
            var header = kind == "FB" ? $"{keyword} \"{name}\"" : $"{keyword} \"{name}\" : Void";

            switch (language)
            {
                case ProgrammingLanguage.SCL:
                    WriteSourceText(softwarePath, groupPath,
                        $"{header}\r\n{{ S7_Optimized_Access := 'TRUE' }}\r\nVERSION : 0.1\r\n\r\nBEGIN\r\nEND_{keyword}\r\n", "scl", false);
                    break;

                case ProgrammingLanguage.STL:
                    WriteSourceText(softwarePath, groupPath,
                        $"{header}\r\n{{ S7_Optimized_Access := 'TRUE' }}\r\nVERSION : 0.1\r\n\r\nBEGIN\r\nNETWORK\r\nTITLE =\r\n\r\nEND_{keyword}\r\n", "awl", false);
                    break;

                case ProgrammingLanguage.LAD:
                case ProgrammingLanguage.FBD:
                    var sections = kind == "FB"
                        ? "<Section Name=\"Input\" /><Section Name=\"Output\" /><Section Name=\"InOut\" /><Section Name=\"Static\" /><Section Name=\"Temp\" /><Section Name=\"Constant\" />"
                        : "<Section Name=\"Input\" /><Section Name=\"Output\" /><Section Name=\"InOut\" /><Section Name=\"Temp\" /><Section Name=\"Constant\" /><Section Name=\"Return\"><Member Name=\"Ret_Val\" Datatype=\"Void\" /></Section>";
                    var lang = language.ToString();
                    var xml =
                        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Document>\r\n" +
                        $"  <Engineering version=\"V{Engineering.TiaMajorVersion}\" />\r\n" +
                        $"  <SW.Blocks.{kind} ID=\"0\">\r\n    <AttributeList>\r\n" +
                        $"      <Interface><Sections xmlns=\"http://www.siemens.com/automation/Openness/SW/Interface/v5\">{sections}</Sections></Interface>\r\n" +
                        "      <MemoryLayout>Optimized</MemoryLayout>\r\n" +
                        $"      <Name>{System.Security.SecurityElement.Escape(name)}</Name>\r\n" +
                        "      <Namespace />\r\n" +
                        $"      <Number>{number}</Number>\r\n" +
                        $"      <ProgrammingLanguage>{lang}</ProgrammingLanguage>\r\n" +
                        "    </AttributeList>\r\n    <ObjectList>\r\n" +
                        "      <SW.Blocks.CompileUnit ID=\"1\" CompositionName=\"CompileUnits\">\r\n" +
                        $"        <AttributeList><NetworkSource /><ProgrammingLanguage>{lang}</ProgrammingLanguage></AttributeList>\r\n" +
                        "      </SW.Blocks.CompileUnit>\r\n    </ObjectList>\r\n" +
                        $"  </SW.Blocks.{kind}>\r\n</Document>\r\n";
                    WriteSourceText(softwarePath, groupPath, xml, "xml", false);
                    break;

                default:
                    throw new PortalException(PortalErrorCode.NotSupported,
                        $"Creating an empty {kind} in '{language}' is not supported. Use LAD, FBD, SCL or STL, or import SimaticML with 'WriteBlockSource' (format 'xml').");
            }

            return group.Blocks.Find(name);
        }

        #endregion

        // From the former Portal.MoveCopy.cs:
        // Copy and move for program blocks and PLC data types.
        // CopyType, MoveType and TransferType live in Portal.Types.cs; the guards and temp-directory
        // helpers below are shared by both.
        //
        // Callers: the CopyBlock / MoveBlock / CopyType / MoveType tools in
        // McpServer.Blocks.cs and McpServer.Types.cs, registered only under '--allow-write'. Affected API: none
        // existing - all members are new. File I/O: each call writes one temporary .xml under the
        // OS temp directory and removes it in a finally block; no caller-visible file is produced.
        //
        // Openness has no move or copy API for blocks and types: PlcBlockComposition.CreateFrom
        // only accepts a library MasterCopy or a library type version, never another block. These
        // operations are therefore composed from export -> import -> (for move) delete the source.
        // Two consequences the caller sees:
        // - the object must be consistent, because TIA Portal refuses to export inconsistent ones;
        // - the exported XML carries the block number, so importing into the same PLC can hit a
        // number collision, which surfaces as ImportFailed rather than being pre-validated.
        // Renaming during a copy is deliberately not offered - it would mean rewriting the XML.

        #region guards

        /// <summary>
        /// A freshly imported or edited object is inconsistent until compiled, and TIA refuses to export
        /// it - so copy/move compile it first instead of failing with 'compile the software first'.
        /// </summary>
        private static void CompileIfInconsistent(IEngineeringServiceProvider obj)
        {
            var consistent = obj is PlcBlock b ? b.IsConsistent : obj is PlcType t ? t.IsConsistent : true;

            if (!consistent)
            {
                obj.GetService<global::Siemens.Engineering.Compiler.ICompilable>()?.Compile();
            }
        }

        private static void EnsureConsistent(PlcBlock block)
        {
            if (!block.IsConsistent)
            {
                throw new PortalException(PortalErrorCode.InvalidState,
                    $"Block '{block.Name}' is inconsistent and TIA Portal will not export it. Compile the software first.");
            }
        }

        private static void EnsureConsistent(PlcType type)
        {
            if (!type.IsConsistent)
            {
                throw new PortalException(PortalErrorCode.InvalidState,
                    $"Type '{type.Name}' is inconsistent and TIA Portal will not export it. Compile the software first.");
            }
        }

        private static void EnsureDifferentGroup(string sourceGroupPath, string targetGroupPath, string itemName)
        {
            var source = NormalizeGroupPath(sourceGroupPath);
            var target = NormalizeGroupPath(targetGroupPath);

            if (source.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"'{itemName}' already lives in '{(target.Length == 0 ? "the root group" : target)}'. " +
                    "Choose a different target group, or use the rename tool to change its name.");
            }
        }

        #endregion

        #region temp scaffolding

        /// <summary>
        /// A private directory per call, so two concurrent transfers of same-named objects
        /// cannot collide on the intermediate file.
        /// </summary>
        private static string CreateTempExportDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "tiamcp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private void DeleteTempExportDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch (Exception ex)
            {
                // A leftover temp file must never fail an otherwise successful copy or move.
                _logger?.LogWarning(ex, "Could not remove temporary export directory {Directory}", directory);
            }
        }

        #endregion

        #region blocks

        /// <param name="overwrite">Replace an object of the same name already in the target group.</param>
        /// <param name="newName">Name of the copy. Empty: the first free '&lt;name&gt;_N' (block names are unique per CPU, so a copy can never keep its name).</param>
        public PlcBlock CopyBlock(string softwarePath, string blockPath, string targetGroupPath, bool overwrite = false, string newName = "")
        {
            return Operation.Run(_logger, nameof(CopyBlock), PortalErrorCode.ImportFailed,
                () => CopyBlockCore(softwarePath, blockPath, targetGroupPath, overwrite, newName),
                ("softwarePath", softwarePath), ("blockPath", blockPath), ("targetGroupPath", targetGroupPath), ("newName", newName));
        }

        public PlcBlock MoveBlock(string softwarePath, string blockPath, string targetGroupPath, bool overwrite = false)
        {
            return Operation.Run(_logger, nameof(MoveBlock), PortalErrorCode.ImportFailed,
                () => MoveBlockCore(softwarePath, blockPath, targetGroupPath, overwrite),
                ("softwarePath", softwarePath), ("blockPath", blockPath), ("targetGroupPath", targetGroupPath));
        }

        private PlcBlock CopyBlockCore(string softwarePath, string blockPath, string targetGroupPath, bool overwrite, string newName)
        {
            var source = GetBlock(softwarePath, blockPath)
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"Block not found at '{blockPath}'. Use 'GetBlocks' to list the available blocks.");

            EnsureNotKnowHowProtected(source);
            CompileIfInconsistent(source);
            EnsureConsistent(source);

            var targetGroup = GetPlcBlockGroupByPath(softwarePath, targetGroupPath)
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"Block group not found at '{targetGroupPath}'. Use 'GetSoftwareTree' to discover valid group paths.");

            var name = string.IsNullOrWhiteSpace(newName) ? UniqueCopyName(softwarePath, source.Name) : newName.Trim();
            EnsureValidName(name);

            var existing = targetGroup.Blocks.Find(name);
            if (BlockOrTypeNameExists(softwarePath, name) && !(overwrite && existing != null))
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"A block or PLC data type named '{name}' already exists in '{softwarePath}'. Pass another 'newName'" +
                    (existing != null ? " or overwrite=true." : "."));
            }

            // Overwriting keeps the number of the replaced block, a new copy takes the first free one.
            var number = existing != null ? existing.Number : NextFreeBlockNumber(softwarePath, BlockNumberCategory(source));
            var directory = CreateTempExportDirectory();

            try
            {
                var file = new FileInfo(Path.Combine(directory, FileNames.Safe(source.Name) + ".xml"));

                source.Export(file, ExportOptions.None);
                RewriteSimaticMlIdentity(file.FullName, name, number);
                targetGroup.Blocks.Import(file, overwrite ? ImportOptions.Override : ImportOptions.None);
            }
            finally
            {
                DeleteTempExportDirectory(directory);
            }

            return targetGroup.Blocks.Find(name)
                ?? throw new PortalException(PortalErrorCode.ImportFailed,
                    $"Block '{name}' was imported into '{targetGroupPath}' but could not be found afterwards.");
        }

        /// <summary>
        /// Openness has no move: export, delete the original (its name is unique per CPU, so the
        /// import would collide while it exists), import into the target group. A failed import puts
        /// the exported original back into its old group.
        /// </summary>
        private PlcBlock MoveBlockCore(string softwarePath, string blockPath, string targetGroupPath, bool overwrite)
        {
            var source = GetBlock(softwarePath, blockPath)
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"Block not found at '{blockPath}'. Use 'GetBlocks' to list the available blocks.");

            EnsureNotKnowHowProtected(source);
            CompileIfInconsistent(source);
            EnsureConsistent(source);

            var sourceGroup = source.Parent as PlcBlockGroup
                ?? throw new PortalException(PortalErrorCode.InvalidState, $"Block '{source.Name}' has no parent group.");
            var sourceGroupPath = GetPlcBlockGroupPath(sourceGroup, false);

            EnsureDifferentGroup(sourceGroupPath, targetGroupPath, source.Name);

            var targetGroup = GetPlcBlockGroupByPath(softwarePath, targetGroupPath)
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"Block group not found at '{targetGroupPath}'. Use 'GetSoftwareTree' to discover valid group paths.");

            var name = source.Name;

            if (targetGroup.Blocks.Find(name) != null && !overwrite)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"'{targetGroupPath}' already contains '{name}'. Pass overwrite=true to replace it.");
            }

            var directory = CreateTempExportDirectory();

            try
            {
                var file = new FileInfo(Path.Combine(directory, FileNames.Safe(name) + ".xml"));

                source.Export(file, ExportOptions.None);
                source.Delete();

                try
                {
                    targetGroup.Blocks.Import(file, overwrite ? ImportOptions.Override : ImportOptions.None);
                }
                catch (Exception importError)
                {
                    try
                    {
                        sourceGroup.Blocks.Import(file, ImportOptions.Override);
                    }
                    catch (Exception restoreError)
                    {
                        // Normal inside a TIA transaction: the failed import poisons it, and TIA then
                        // rolls the whole step (including the delete) back.
                        throw new PortalException(PortalErrorCode.ImportFailed,
                            $"Moving '{name}' to '{targetGroupPath}' failed: {importError.Message}. The step runs in a TIA Portal transaction that is rolled back, " +
                            $"so the block normally stays in '{sourceGroupPath}' - check with 'GetBlocks' (explicit restore: {restoreError.Message}).", null, importError);
                    }

                    throw new PortalException(PortalErrorCode.ImportFailed,
                        $"Moving '{name}' to '{targetGroupPath}' failed, the block was restored in '{sourceGroupPath}': {importError.Message}", null, importError);
                }
            }
            finally
            {
                DeleteTempExportDirectory(directory);
            }

            return targetGroup.Blocks.Find(name)
                ?? throw new PortalException(PortalErrorCode.ImportFailed,
                    $"Block '{name}' was imported into '{targetGroupPath}' but could not be found afterwards.");
        }

        /// <summary>
        /// Sets name (and, for blocks, number) of the exported object in a SimaticML file, so the
        /// import creates a new object instead of colliding with the original.
        /// </summary>
        internal static void RewriteSimaticMlIdentity(string file, string? newName, int? newNumber)
        {
            var doc = System.Xml.Linq.XDocument.Load(file, System.Xml.Linq.LoadOptions.PreserveWhitespace);
            var objectElement = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName.StartsWith("SW.", StringComparison.Ordinal))
                ?? throw new PortalException(PortalErrorCode.ExportFailed, "The exported SimaticML has no block or type element.");
            var attributes = objectElement.Element("AttributeList")
                ?? throw new PortalException(PortalErrorCode.ExportFailed, "The exported SimaticML has no AttributeList.");

            if (!string.IsNullOrEmpty(newName))
            {
                var nameElement = attributes.Element("Name")
                    ?? throw new PortalException(PortalErrorCode.ExportFailed, "The exported SimaticML has no Name.");
                var oldName = nameElement.Value;
                nameElement.Value = newName;

                // An SCL function assigns its return value through its own name ('#FC_X := ...'),
                // exported as a LocalVariable access named like the FC. Renamed copies must follow,
                // otherwise the copy fails to compile with "Tag #FC_X not defined".
                if (objectElement.Name.LocalName == "SW.Blocks.FC")
                {
                    foreach (var access in objectElement.Descendants().Where(e =>
                                 e.Name.LocalName == "Access" && (string?)e.Attribute("Scope") == "LocalVariable"))
                    {
                        var component = access.Descendants().FirstOrDefault(e => e.Name.LocalName == "Component");

                        if (component != null && (string?)component.Attribute("Name") == oldName)
                        {
                            component.SetAttributeValue("Name", newName);
                        }
                    }
                }
            }

            if (newNumber.HasValue)
            {
                var numberElement = attributes.Element("Number");
                if (numberElement != null)
                {
                    numberElement.Value = newNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            doc.Save(file, System.Xml.Linq.SaveOptions.DisableFormatting);
        }

        #endregion

        #region interface

        /// <summary>
        /// The members of a data block: every entry with its attributes (data type, start value,
        /// comment, retain, accessibility - Openness exposes these as attributes rather than
        /// typed properties, so they are reported as-is and stay correct across versions).
        /// Needs no export and works on inconsistent blocks.
        ///
        /// Data blocks only: V21 Openness puts 'Interface' on DataBlock and offers no equivalent
        /// accessor on CodeBlock, so the VAR_INPUT/VAR_OUTPUT declarations of an FB, FC or OB
        /// have to be read from the declaration part of GetSourceBlock or GetDocumentsBlock instead.
        /// </summary>
        public List<InterfaceMember> GetBlockInterface(string softwarePath, string blockPath)
        {
            return Operation.Run(_logger, nameof(GetBlockInterface), PortalErrorCode.NotFound,
                () =>
                {
                    var block = GetBlock(softwarePath, blockPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Block not found at '{blockPath}'. Use 'ResolveObjectPath' or 'GetBlocks' to find its path.");

                    if (block is not DataBlock dataBlock)
                    {
                        // Openness has no interface accessor on code blocks: read the <Interface> of
                        // the SimaticML export instead (needs a consistent block).
                        if (!block.IsConsistent)
                        {
                            throw new PortalException(PortalErrorCode.InvalidState,
                                $"'{block.Name}' is inconsistent, so its interface cannot be exported. Compile it first ('CompileCodeBlock').");
                        }

                        return ReadCodeBlockInterface(softwarePath, blockPath);
                    }

                    var members = dataBlock.Interface?.Members
                        ?? throw new PortalException(PortalErrorCode.NotSupported,
                            $"Data block '{block.Name}' exposes no members. Know-how protected blocks hide theirs.");

                    return members.Select(ToInterfaceMember).ToList();
                },
                ("softwarePath", softwarePath), ("blockPath", blockPath));
        }

        /// <summary>
        /// Interface of an FB, FC or OB from its SimaticML export: one entry per member, nested
        /// struct/UDT members flattened as 'Parent.Child'. Attributes: Section, StartValue, Comment,
        /// Informative (system-provided OB parameters).
        /// </summary>
        private List<InterfaceMember> ReadCodeBlockInterface(string softwarePath, string blockPath)
        {
            var xml = GetXmlBlock(softwarePath, blockPath, int.MaxValue).Text;
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            var result = new List<InterfaceMember>();

            foreach (var section in doc.Descendants().Where(e => e.Name.LocalName == "Section"))
            {
                var sectionName = (string?)section.Attribute("Name") ?? string.Empty;

                foreach (var member in section.Elements().Where(e => e.Name.LocalName == "Member"))
                {
                    AddXmlMember(result, member, sectionName, string.Empty);
                }
            }

            return result;
        }

        private static void AddXmlMember(List<InterfaceMember> result, System.Xml.Linq.XElement member, string section, string prefix)
        {
            var name = prefix + ((string?)member.Attribute("Name") ?? string.Empty);
            var dataType = (string?)member.Attribute("Datatype") ?? string.Empty;
            var info = new InterfaceMember { Name = name, DataTypeName = dataType };

            info.Attributes["Section"] = section;
            info.Attributes["DataTypeName"] = dataType;

            var startValue = member.Elements().FirstOrDefault(e => e.Name.LocalName == "StartValue");
            if (startValue != null)
            {
                info.Attributes["StartValue"] = startValue.Value;
            }

            var comment = member.Elements().FirstOrDefault(e => e.Name.LocalName == "Comment");
            if (comment != null)
            {
                var text = string.Join(" | ", comment.Elements().Where(e => e.Name.LocalName == "MultiLanguageText").Select(e => e.Value).Where(v => v.Length > 0));
                if (text.Length > 0)
                {
                    info.Attributes["Comment"] = text;
                }
            }

            if ((string?)member.Attribute("Informative") == "true")
            {
                info.Attributes["Informative"] = "true";
            }

            result.Add(info);

            // Struct members are nested directly; members of a UDT/instance are informative copies
            // inside <Sections>, which are skipped to keep the list readable.
            foreach (var child in member.Elements().Where(e => e.Name.LocalName == "Member"))
            {
                AddXmlMember(result, child, section, name + ".");
            }
        }

        private static InterfaceMember ToInterfaceMember(Member member)
        {
            var info = new InterfaceMember { Name = member.Name };

            foreach (var attribute in member.GetAttributeInfos())
            {
                try
                {
                    var value = member.GetAttribute(attribute.Name);

                    info.Attributes[attribute.Name] = value?.ToString() ?? string.Empty;
                }
                catch (Exception)
                {
                    // A member can advertise an attribute it cannot currently produce; skipping
                    // one attribute is better than losing the whole signature.
                }
            }

            info.DataTypeName = info.Attributes.TryGetValue("DataTypeName", out var dataType) ? dataType : null;

            return info;
        }

        #endregion
    }

    /// <summary>One member of a block interface.</summary>
    public class InterfaceMember
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Convenience copy of the DataTypeName attribute when the member has one.</summary>
        public string? DataTypeName { get; set; }

        /// <summary>Every attribute Openness reports for the member, stringified.</summary>
        public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
    }
}
