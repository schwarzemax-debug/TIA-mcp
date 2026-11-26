using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering.SW.Blocks;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    [McpServerToolType]
    public static partial class McpServer
    {
        private static IServiceProvider? _services;

        private static Portal? _portal;

        public static ILogger? Logger { get; set; }

        public static Portal Portal
        {
            get
            {
                if (_services !=null)
                {
                    return _services.GetRequiredService<Portal>();
                }
                else
                {
                    if (_portal == null)
                    {
                        _portal = new Portal();
                    }
                    return _portal;
                }
            }
            set
            {
                _portal = value ?? throw new ArgumentNullException(nameof(value), "Portal cannot be null");
            }
        }

        public static void SetServiceProvider(IServiceProvider services)
        {
            _services = services;
        }

        #region portal

        [McpServerTool(Name = "Connect", Title = "Connect to TIA Portal", Destructive = false, Idempotent = true, OpenWorld = false),
         Description("Connect to TIA Portal and return its 'portalId'. Without 'portalId' it attaches to the only running instance, or starts one if none is running; with several running it fails and lists them")]
        public static ResponseConnect Connect(
            [Description("portalId: process id of the TIA Portal instance to attach to (see 'GetPortals'). Leave empty when at most one instance is running")] int? portalId = null)
        {
            Logger?.LogInformation("Connecting to TIA Portal...");

            try
            {
                var attachedId = Portal.AttachPortal(portalId);

                return new ResponseConnect
                {
                    Message = $"Connected to TIA-Portal instance {attachedId}",
                    PortalId = attachedId,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(PortalSelection.Describe(pex), pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error connecting to TIA-Portal: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "Disconnect", Title = "Disconnect from TIA Portal", Destructive = false, Idempotent = true, OpenWorld = false), Description("Disconnect from a TIA Portal instance (the one named by portalId, or the only attached one). Other attached instances stay connected")]
        public static ResponseDisconnect Disconnect()
        {
            try
            {
                if (Portal.DisconnectPortal())
                {
                    return new ResponseDisconnect
                    {
                        Message = "Disconnected from TIA-Portal",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException("Failed disconnecting from TIA-Portal");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error disconnecting from TIA-Portal: {ex.Message}", ex);
            }
        }

        [PortalIndependent]
        [McpServerTool(Name = "GetPortals", Title = "Get portals", ReadOnly = true, OpenWorld = false, UseStructuredContent = true),
         Description("List the running TIA Portal instances with their 'portalId', open project path and whether this server is attached. The top-level 'portalId' is the attached instance tools use by default (empty when none or several are attached). Use it to choose the 'portalId' when more than one instance is running")]
        public static ResponsePortals GetPortals()
        {
            try
            {
                var portals = Portal.GetPortals();

                // the one instance tools use without 'portalId'; none or several attached: no default
                var attached = portals.Where(p => p.IsAttached).Select(p => p.PortalId).ToList();
                int? portalId = attached.Count == 1 ? attached[0] : null;

                return new ResponsePortals
                {
                    Message = $"{portals.Count} TIA-Portal instance(s) running" +
                              (portalId is int id ? $", attached to {id}" : attached.Count > 1 ? $", {attached.Count} attached" : ", none attached"),
                    PortalId = portalId,
                    Portals = portals,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error listing TIA-Portal instances: {ex.Message}", ex);
            }
        }

        #endregion

        #region state

        [McpServerTool(Name = "GetState", Title = "Get server state", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Show the portalId this server is attached to, whether it is connected, the open project and session, and whether write mode (--allow-write) is enabled")]
        public static ResponseState GetState()
        {
            try
            {
                var state = Portal.GetState();

                if (state != null)
                {
                    return new ResponseState
                    {
                        Message = "TIA-Portal MCP server state retrieved",
                        PortalId = state.PortalId,
                        IsConnected = state.IsConnected,
                        Project = state.Project,
                        Session = state.Session,
                        AllowWrite = WritePolicy.AllowWrite,
                        Presets = ToolDefaults.Presets.Count == 0
                            ? null
                            : new JsonObject(ToolDefaults.Presets.Select(p =>
                                new KeyValuePair<string, JsonNode?>(p.Key, JsonValue.Create(p.Value)))),
                        ForcedPresets = ToolDefaults.Forced.Count == 0
                            ? null
                            : ToolDefaults.Forced.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException("Failed to retrieve TIA-Portal MCP server state");
                }
                

            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving TIA-Portal MCP server state: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "Doctor", Title = "Run Doctor diagnostics", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Diagnose the TIA Portal environment: server version, connection, open project, active and installed TIA Portal versions, Openness user group membership")]
        public static ResponseDoctor Doctor()
        {
            Logger?.LogInformation("Running doctor diagnostics...");

            try
            {
                // Fully qualified: 'Diagnostics' alone would collide with the System.Diagnostics namespace.
                var report = TiaMcpServer.Siemens.Diagnostics.Run(Portal, WritePolicy.AllowWrite, CliOptions.Current);

                return new ResponseDoctor
                {
                    Message = "TIA-Portal environment diagnosed",
                    Report = report.Text,
                    ServerVersion = report.ServerVersion,
                    IsConnected = report.IsConnected,
                    ActiveTiaMajorVersion = report.ActiveTiaMajorVersion,
                    ProjectName = report.ProjectName,
                    ProjectPath = report.ProjectPath,
                    IsUserInGroup = report.IsUserInGroup,
                    AllowWrite = report.AllowWrite,
                    Options = new JsonObject(report.Options.Select(o =>
                        new KeyValuePair<string, JsonNode?>(o.Key, JsonValue.Create(o.Value)))),
                    Installations = report.Installations
                        .Select(i => new ResponseTiaInstallation
                        {
                            MajorVersion = i.MajorVersion,
                            InstallPath = i.InstallPath,
                            EngineeringExists = i.EngineeringExists,
                            PortalExeExists = i.PortalExeExists
                        })
                        .ToList(),
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error diagnosing the TIA-Portal environment: {ex.Message}", ex);
            }
        }

        #endregion

        #region project/session

        [McpServerTool(Name = "GetProject", Title = "Get open project", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("List the projects and local sessions open in the connected TIA Portal, with name and path")]
        public static ResponseGetProjects GetProjects()
        {
            try
            {
                var list = Portal.GetProjects();

                list.AddRange(Portal.GetSessions());

                var responseList = new List<ResponseProjectInfo>();
                foreach (var project in list)
                {
                    var attributes = Helper.GetAttributeList(project);

                    if (project != null)
                    {
                        responseList.Add(new ResponseProjectInfo
                        {
                            Name = project.Name,
                            Attributes = attributes
                        });
                    }
                }

                return new ResponseGetProjects
                {
                    Message = "Open projects and sessions retrieved",
                    Items = responseList,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving open projects: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "OpenProject", Title = "Open project/session", Destructive = true, Idempotent = true, OpenWorld = false), Description("Open a local project (.apXX) or local session (.alsXX) in the connected TIA Portal and make it the current project. Closes the project or session that is open first. Needs a prior Connect; OpenTiaProject connects and opens in one call")]
        public static ResponseOpenProject OpenProject(
            [Description("projectpath: full path of the .apXX project or .alsXX session file on the machine running this server")] string projectpath)
        {
            try
            {
                // get project extension
                string extension = Path.GetExtension(projectpath).ToLowerInvariant();

                // use regex to check if extension is .ap\d+ or .als\d+
                if (!Regex.IsMatch(extension, @"^\.ap\d+$") &&
                    !Regex.IsMatch(extension, @"^\.als\d+$"))
                {
                    throw new McpException("Invalid project file extension. Use .apXX for projects or .alsXX for sessions, where XX=18,19,20,....");
                }

                bool success = false;
                bool wasAlreadyOpen = false;

                // An already open project or session is used as it is; the current one is only
                // closed when another has to be opened (Portal.OpenProject/OpenSession decide).
                if (extension.StartsWith(".ap"))
                {
                    success = Portal.OpenProject(projectpath, out wasAlreadyOpen);
                }
                if (extension.StartsWith(".als"))
                {
                    Portal.CloseProject();
                    success = Portal.OpenSession(projectpath, out wasAlreadyOpen);
                }

                if (success)
                {
                    return new ResponseOpenProject
                    {
                        Message = wasAlreadyOpen
                            ? $"Project '{projectpath}' was already open and is used"
                            : $"Project '{projectpath}' opened",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed to open project '{projectpath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error opening project '{projectpath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "SaveProject", Title = "Save project", Destructive = true, Idempotent = true, OpenWorld = false), Description("Save the current local project or local session")]
        public static ResponseSaveProject SaveProject()
        {
            try
            {
                if (Portal.IsLocalSession)
                {
                    if (Portal.SaveSession())
                    {
                        return new ResponseSaveProject
                        {
                            Message = "Local session saved",
                            Meta = new JsonObject
                            {
                                ["timestamp"] = DateTime.Now,
                                ["success"] = true
                            }
                        };
                    }
                    else
                    {
                        throw new McpException("Failed to save local session");
                    }
                }
                else
                {
                    if (Portal.SaveProject())
                    {
                        return new ResponseSaveProject
                        {
                            Message = "Local project saved",
                            Meta = new JsonObject
                            {
                                ["timestamp"] = DateTime.Now,
                                ["success"] = true
                            }
                        };
                    }
                    else
                    {
                        throw new McpException("Failed to save project");
                    }
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error saving local project/session: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "SaveAsProject", Title = "Save project as", Destructive = true, Idempotent = true, OpenWorld = false), Description("Save the current local project to a new location. Not possible for local sessions")]
        public static ResponseSaveAsProject SaveAsProject(
            [Description("newProjectPath: target directory on the machine running this server that receives the saved project")] string newProjectPath)
        {
            try
            {
                if (Portal.IsLocalSession)
                {
                    throw new McpException($"Cannot save local session as '{newProjectPath}'");
                }
                else
                {
                    if (Portal.SaveAsProject(newProjectPath))
                    {
                        return new ResponseSaveAsProject
                        {
                            Message = $"Local project saved as '{newProjectPath}'",
                            Meta = new JsonObject
                            {
                                ["timestamp"] = DateTime.Now,
                                ["success"] = true
                            }
                        };
                    }
                    else
                    {
                        throw new McpException($"Failed saving local project as '{newProjectPath}'");
                    }
                }

            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error saving local project/session as '{newProjectPath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "CloseProject", Title = "Close project", Destructive = true, Idempotent = true, OpenWorld = false), Description("Close the current local project or local session. Save first to keep changes")]
        public static ResponseCloseProject CloseProject()
        {
            try
            {
                bool success;

                if (Portal.IsLocalSession)
                {
                    success = Portal.CloseSession();
                    if (success)
                    {
                        return new ResponseCloseProject
                        {
                            Message = "Local session closed",
                            Meta = new JsonObject
                            {
                                ["timestamp"] = DateTime.Now,
                                ["success"] = true
                            }
                        };
                    }
                    else
                    {
                        throw new McpException("Failed closing local session");
                    }
                }
                else
                {
                    success = Portal.CloseProject();
                    if (success)
                    {
                        return new ResponseCloseProject
                        {
                            Message = "Local project closed",
                            Meta = new JsonObject
                            {
                                ["timestamp"] = DateTime.Now,
                                ["success"] = true
                            }
                        };
                    }
                    else
                    {
                        throw new McpException("Failed closing project");
                    }
                }

            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error closing local project/session: {ex.Message}", ex);
            }
        }

        #endregion

        #region devices

        [McpServerTool(Name = "GetProjectTree", Title = "Get project tree", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Show the structure of the current project or session as a tree of devices, device items, groups and PLC/HMI software. Use it to find the device and software paths other tools expect")]
        public static ResponseProjectTree GetProjectTree()
        {
            try
            {
                var tree = Portal.GetProjectTree();

                if (!string.IsNullOrEmpty(tree))
                {
                    return new ResponseProjectTree
                    {
                        Message = "Project tree retrieved",
                        Tree = "```\n" + tree + "\n```",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException("Failed retrieving project tree");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving project tree: {ex.Message}", ex);
            }
        }

        #endregion

        #region lookup

        [McpServerTool(Name = "OpenTiaProject", Title = "Connect/open a project", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Connect to TIA Portal if not already connected and return the 'portalId' plus the device and PLC software paths the other tools need. Without 'projectpath' (the normal case) it works on the project or session that is open in TIA Portal at this moment and opens or closes nothing. With 'projectpath' it opens that project or session instead. Replaces the Connect then OpenProject then GetProjectTree sequence")]
        public static async Task<ResponseOpenTiaProject> OpenTiaProject(
            [Description("projectpath: leave empty to use the project currently open in TIA Portal. Only when the user explicitly names another project: full path of the .apXX project or .alsXX session file on the machine running this server")] string projectpath = "",
            [Description("portalId: process id of the TIA Portal instance to open it in (see 'GetPortals'). Leave empty when at most one instance is running")] int? portalId = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                int attachedId;
                bool wasAttached;

                try
                {
                    // Attaching is unscoped, as in 'Connect': the instance may not be attached yet,
                    // which is why this tool declares 'portalId' itself and bypasses the filter.
                    attachedId = Portal.AttachPortal(portalId, out wasAttached);
                }
                catch (Exception ex) when (ex is not PortalException)
                {
                    // TIA Portal asks once per new server program in the dialog 'Openness access'
                    // (often behind its main window); while it waits, or after it timed out, the
                    // attach fails. Say so, with TIA Portal's own reason, instead of a bare "failed".
                    throw new McpException(
                        $"Failed to connect to TIA Portal: {ImportErrors.Describe(ex)} " +
                        "On the first call of a new server program TIA Portal shows the dialog 'Openness access' (often behind its main window): " +
                        "ask the user to confirm it there, then call 'TiaConnect' again. Otherwise run the 'Doctor' tool to check the installation and user group membership.", ex);
                }

                // Everything after attaching runs scoped to that instance and under its lock.
                return await Portal.RunScopedAsync(
                    attachedId,
                    () => new ValueTask<ResponseOpenTiaProject>(OpenAndDescribe(projectpath, attachedId, wasAttached)),
                    cancellationToken);
            }
            catch (PortalException pex)
            {
                throw new McpException(PortalSelection.Describe(pex), pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error opening '{projectpath}': {ex.Message}", ex);
            }
        }

        private static ResponseOpenTiaProject OpenAndDescribe(string path, int portalId, bool wasAttached)
        {
            string openedMessage;

            if (string.IsNullOrWhiteSpace(path))
            {
                // Default: the project the user has open in TIA Portal right now.
                if (Portal.UseOpenProject(out var openName, out var openPath))
                {
                    path = openPath ?? openName ?? string.Empty;
                    openedMessage = $"Using the project open in TIA Portal: '{openName}' ({openPath ?? "path unknown"})";
                }
                else if (!string.IsNullOrWhiteSpace(CliOptions.Current.ProjectPath))
                {
                    // Nothing open: fall back to the configured '--project-path', if any.
                    path = CliOptions.Current.ProjectPath!;
                    openedMessage = OpenProject(path).Message + " (no project was open; configured --project-path used)";
                }
                else
                {
                    throw new McpException(
                        $"No project is open in TIA-Portal instance {portalId}. Ask the user to open the project in TIA Portal, or pass 'projectpath'.");
                }
            }
            else
            {
                // Reuse the existing tool rather than duplicating its extension validation and
                // project/session branching; it also closes whatever was open first.
                openedMessage = OpenProject(path).Message;
            }

            var softwarePaths = CollectSoftwarePaths();

            return new ResponseOpenTiaProject
            {
                Message = $"{openedMessage} in TIA-Portal instance {portalId}. PLC software: " +
                          (softwarePaths.Count > 0 ? string.Join(", ", softwarePaths) : "none found"),
                PortalId = portalId,
                ProjectPath = path,
                WasAlreadyConnected = wasAttached,
                SoftwarePaths = softwarePaths,
                Tree = Portal.GetProjectTree(),
                Meta = Ok(new JsonObject { ["softwareCount"] = softwarePaths.Count })
            };
        }

        #endregion

        // From the former McpServer.Preview.cs:
        // Looking before writing.
        //
        // Callers: the MCP host, through tool registration. Affected API: none - PreviewImport is
        // new and no write tool changed. File I/O: reads the caller's import directory; writes
        // nothing, and never touches the project.
        //
        // This is the read-only half of write safety; the other half is the transaction wrapper in
        // Portal.cs (InTransaction), which every write tool now runs inside. It is one tool rather
        // than a 'dryRun' flag on all 39 write tools: the flag would have changed the execution
        // path of every write for a report that only imports really need, and imports are where
        // the collisions actually happen.
        //
        // What it can and cannot know: the object name is taken from the file name, which is how
        // every exporter in this server names its output. A hand-edited file whose content
        // declares a different name than its file name would be reported under the file name.

        #region preview

        [McpServerTool(Name = "PreviewImport", Title = "Preview import", ReadOnly = true, OpenWorld = false, UseStructuredContent = true),
         Description("Report what importing a directory would create, overwrite or collide with, without touching the project. Checks each file against the objects already in the PLC, including the rule that a PLC data type name must be unique across the whole PLC - importing an existing type name into a different group fails even with importOption 'Override'")]
        public static ResponseImportPreview PreviewImport(
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath,
            [Description("importPath: directory holding the files to import (.s7dcl source documents or .xml); read from <importPath>/<softwarePath>/")] string importPath,
            [Description("kind: what the files contain - 'type' for PLC data types, 'block' for program blocks")] string kind,
            [Description("groupPath: the group the import would target; empty means the root of that area. A leading system folder segment is accepted")] string groupPath = "")
        {
            try
            {
                importPath = Siemens.Portal.ResolveSoftwareRoot(importPath, softwarePath);

                if (!Directory.Exists(importPath))
                {
                    throw new McpException($"Import directory '{importPath}' does not exist.");
                }

                // Accept the plural too ("blocks", "types") - agents naturally name the folder.
                var normalizedKind = (kind ?? string.Empty).Trim().TrimEnd('s', 'S');
                var isType = normalizedKind.Equals("type", StringComparison.OrdinalIgnoreCase);

                if (!isType && !normalizedKind.Equals("block", StringComparison.OrdinalIgnoreCase))
                {
                    throw new McpException($"Unknown kind '{kind}'. Use 'type' or 'block'.");
                }

                // Recursive: with --keep-structure the exports live in '<system folder>/<groups>/',
                // and the old top-level-only scan found nothing. Each file is classified by
                // extension or, for SimaticML, by its root object, so tag tables and watch tables
                // in the same tree are not mistaken for blocks.
                // A file in a subfolder lands in that group (below groupPath), as the program
                // import does it; the leading system folder of a keepStructure export is dropped.
                var systemRoot = Portal.SystemRootName(softwarePath, isType);
                groupPath = Siemens.Portal.WithoutSystemRoot(systemRoot, groupPath);
                var files = Directory
                    .GetFiles(importPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => PreviewMatchesKind(f, isType))
                    .Select(f => new
                    {
                        Name = Path.GetFileNameWithoutExtension(f),
                        Group = JoinGroup(groupPath, Siemens.Portal.ImportGroupOfFile(systemRoot, importPath, f))
                    })
                    .Where(f => !string.IsNullOrEmpty(f.Name))
                    .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var target = string.IsNullOrEmpty(groupPath) ? "the root" : groupPath;
                var items = new List<ResponseImportPreviewItem>();

                foreach (var file in files)
                {
                    var name = file.Name;
                    var fileGroup = file.Group;
                    var existing = Portal.ResolveObjectPath(softwarePath, name, isType ? "type" : "block")
                        .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

                    if (existing == null)
                    {
                        items.Add(new ResponseImportPreviewItem { Name = name, Effect = "create", TargetPath = Join(fileGroup, name) });

                        continue;
                    }

                    // Both sides are free of the system folder here, so an exact compare is right;
                    // IsSameGroup's segment-stripping fallback would match 'Test/A' against 'A'.
                    var sameGroup = string.Equals(Parent(existing.Path ?? string.Empty), fileGroup, StringComparison.OrdinalIgnoreCase);

                    items.Add(new ResponseImportPreviewItem
                    {
                        Name = name,
                        Effect = sameGroup ? "overwrite" : (isType ? "conflict" : "overwrite-elsewhere"),
                        TargetPath = Join(fileGroup, name),
                        ExistingPath = existing.Path,
                        Note = sameGroup
                            ? "Already exists in the target group; importOption 'Override' replaces it."
                            : isType
                                ? "A PLC data type name must be unique across the whole PLC. This import fails even with " +
                                  $"'Override' - target '{Parent(existing.Path)}' instead, or rename the type."
                                : $"A block of this name already exists at '{existing.Path}'; importing here may collide on the block number."
                    });
                }

                var creates = items.Count(i => i.Effect == "create");
                var overwrites = items.Count(i => i.Effect == "overwrite");
                var conflicts = items.Count(i => i.Effect == "conflict" || i.Effect == "overwrite-elsewhere");

                return new ResponseImportPreview
                {
                    Message = $"Importing '{importPath}' into {target} would create {creates}, overwrite {overwrites}, " +
                              $"and hit {conflicts} conflict(s). Nothing was changed.",
                    Items = items,
                    CreateCount = creates,
                    OverwriteCount = overwrites,
                    ConflictCount = conflicts,
                    Meta = Ok(new JsonObject
                    {
                        ["files"] = files.Count,
                        ["create"] = creates,
                        ["overwrite"] = overwrites,
                        ["conflict"] = conflicts
                    })
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error previewing the import of '{importPath}': {ex.Message}", ex);
            }
        }

        /// <summary>File kinds an export of this server produces, and therefore an import consumes.</summary>
        private static readonly HashSet<string> PreviewExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".s7dcl", ".xml", ".scl", ".awl", ".db", ".udt" };

        /// <summary>Whether an import file holds program blocks (isType false) or PLC data types (isType true).</summary>
        private static bool PreviewMatchesKind(string file, bool isType)
        {
            var extension = Path.GetExtension(file);

            if (!PreviewExtensions.Contains(extension))
            {
                return false;
            }

            switch (extension.ToLowerInvariant())
            {
                case ".udt":
                    return isType;
                case ".scl":
                case ".awl":
                case ".db":
                    return !isType;
                case ".xml":
                    try
                    {
                        // The root object element sits right after DocumentInfo; 8 KB always reach it.
                        using (var reader = new StreamReader(file))
                        {
                            var buffer = new char[8192];
                            var head = new string(buffer, 0, reader.Read(buffer, 0, buffer.Length));

                            return isType ? head.Contains("<SW.Types.") : head.Contains("<SW.Blocks.");
                        }
                    }
                    catch (IOException)
                    {
                        return false;
                    }
                default:
                    return true; // .s7dcl: kind not visible from outside
            }
        }

        private static string Join(string groupPath, string name) =>
            string.IsNullOrEmpty(groupPath) ? name : $"{groupPath.TrimEnd('/')}/{name}";

        private static string JoinGroup(string groupPath, string subGroup) =>
            string.IsNullOrEmpty(subGroup) ? (groupPath ?? string.Empty) : Join(groupPath, subGroup);

        private static string Parent(string path)
        {
            var slash = path.LastIndexOf('/');

            return slash < 0 ? string.Empty : path.Substring(0, slash);
        }

        /// <summary>
        /// True when the resolved object path lives in the group named by groupPath. A groupPath
        /// copied from a keepStructure export starts with the localised system folder
        /// ('PLC data types', 'Program blocks'), which the resolved paths do not carry; a plain
        /// groupPath does not. The folder name is localised, so both forms are tried instead of
        /// matching a fixed name.
        /// </summary>
        public static bool IsSameGroup(string existingPath, string groupPath)
        {
            var existingGroup = Parent(existingPath ?? string.Empty);
            groupPath ??= string.Empty;

            if (string.Equals(existingGroup, groupPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // A lone segment is a real group name; only a segment followed by '/' can be a system folder.
            var slash = groupPath.IndexOf('/');

            return slash >= 0
                && string.Equals(existingGroup, groupPath.Substring(slash + 1), StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        // From the former McpServerWrite.cs:
        // The project-mutating MCP tools (create, rename, delete, import into the project).
        //
        // Callers: registered by Program.BuildTools() only when '--allow-write' was passed, and
        // invoked directly by the write test classes. Affected API: none existing. Data: returns the
        // shared ResponseCreated / ResponseDeleted / ResponseRenamed / ResponseImported /
        // ResponseGenerateBlocks DTOs. The import tools read a caller-supplied file; nothing here
        // writes files. Project changes live in memory until SaveProject (or SaveSession).
        //
        // Conventions, enforced by the Guarded helper below:
        // - WritePolicy.EnsureEnabled runs first, because these are public static methods that the
        // MSTest suite calls directly, bypassing tool registration.
        // - A PortalException is surfaced with its own guidance message; anything else is wrapped.
        // - Destructive = true; Idempotent = true only for renames and deletes-by-name.
        //
        // Filesystem-only exports stay in McpServer: they never modify the project.

        #region plumbing (write)

        /// <summary>
        /// Reminds the caller that Openness edits are in-memory, naming the right save tool for
        /// the current mode: a multiuser local session saves through SaveSession, not SaveProject.
        /// </summary>
        private static string SaveHint =>
            Portal.IsLocalSession
                ? "The change is in memory; call 'SaveSession' to persist it."
                : "The change is in memory; call 'SaveProject' to persist it.";

        private static T Guarded<T>(string toolName, Func<T> body)
        {
            WritePolicy.EnsureEnabled(toolName);

            try
            {
                // One transaction per tool call: the edits commit together or not at all, and
                // the operator sees a single named entry in the TIA Portal undo stack instead of
                // an unlabelled pile of steps. Falls back to an unwrapped write when TIA Portal
                // refuses exclusive access, so this can never turn a working write into a
                // failure - see Portal.InTransaction in Portal.cs.
                return Portal.InTransaction($"MCP: {toolName}", body);
            }
            catch (PortalException pex)
            {
                // PortalException messages are already written for the caller (what went wrong
                // and which tool lists the valid paths), so they pass through unchanged.
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error in '{toolName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Like <see cref="Guarded{T}"/> for tools that import many files: exclusive access for the
        /// whole call, but one transaction per file (Portal.InStep), so a failing file is rolled
        /// back alone and reported with its own reason instead of poisoning the commit of all files.
        /// </summary>
        private static T GuardedSteps<T>(string toolName, Func<T> body)
        {
            WritePolicy.EnsureEnabled(toolName);

            try
            {
                return Portal.InExclusiveAccess($"MCP: {toolName}", body);
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error in '{toolName}': {ex.Message}", ex);
            }
        }

        private static JsonObject OkMeta() => new JsonObject
        {
            ["timestamp"] = DateTime.Now,
            ["success"] = true,
            ["pendingSave"] = true
        };

        private static ResponseCreated Created(string kind, string name, string path) => new ResponseCreated
        {
            Kind = kind,
            Name = name,
            Path = path,
            Message = $"{kind} '{name}' created. {SaveHint}",
            Meta = OkMeta()
        };

        private static ResponseDeleted Deleted(string kind, string path) => new ResponseDeleted
        {
            Kind = kind,
            Path = path,
            Message = $"{kind} '{path}' deleted. {SaveHint}",
            Meta = OkMeta()
        };

        private static ResponseRenamed Renamed(string kind, string oldPath, string newName, string newPath) => new ResponseRenamed
        {
            Kind = kind,
            OldPath = oldPath,
            NewName = newName,
            NewPath = newPath,
            Message = $"{kind} '{oldPath}' renamed to '{newName}'. {SaveHint}",
            Meta = OkMeta()
        };

        private static ResponseImported Imported(string kind, string groupPath, string importPath) => new ResponseImported
        {
            Kind = kind,
            GroupPath = groupPath,
            ImportPath = importPath,
            Message = $"{kind} imported from '{importPath}' into '{groupPath}'. {SaveHint}",
            Meta = OkMeta()
        };

        /// <summary>Replaces the last segment of a path, for reporting a rename's new path.</summary>
        private static string ReplaceLeaf(string path, string newName)
        {
            var trimmed = (path ?? string.Empty).Trim('/');
            var index = trimmed.LastIndexOf('/');

            return index < 0 ? newName : trimmed.Substring(0, index + 1) + newName;
        }

        private static string JoinPath(string groupPath, string name) =>
            string.IsNullOrEmpty(groupPath) ? name : $"{groupPath}/{name}";

        #endregion
    }
}
