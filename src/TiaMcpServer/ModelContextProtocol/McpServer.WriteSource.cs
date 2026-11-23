using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TiaMcpServer.Siemens;
using SdkMcpServer = global::ModelContextProtocol.Server.McpServer;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        // WriteBlockSource: an agent hands over the source text of a block or PLC data type and
        // gets it back as a compiled object in the open project, plus the compiler messages.
        // That closes the gap between 'CreateFB' (empty ProDiag FB only in V20) and the file
        // based imports (export folder, git, confirmation per import).
        //
        // Callers: the MCP host, only under '--allow-write'. Affected API: none existing - new tool.
        // Project-mutating, so it asks the user once per server session (MCP elicitation) before
        // the first write. A client without elicitation is refused unless the server runs with
        // '--trust-writes', which only the person who configures the server can set.

        #region write block source

        private static readonly object TrustLock = new object();

        /// <summary>True once the user confirmed in this server session, or when started with --trust-writes.</summary>
        private static bool _writeSourceTrusted;

        [WriteTool]
        [McpServerTool(Name = "WriteBlockSource", Title = "Write block source", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use this to create or replace blocks and PLC data types directly from source text, in one call: the text is compiled into the project and, by default, the new blocks are compiled and their compiler messages returned. This is the way to build program parts as an agent: no export folder, no git. Send the COMPLETE source file content in 'sourceText'. The object name comes from the source itself (FUNCTION_BLOCK \"FB_Name\", FUNCTION \"FC_Name\", ORGANIZATION_BLOCK \"OB_Name\", DATA_BLOCK \"DB_Name\", TYPE \"UDT_Name\"), not from a parameter. 'format': 'scl' (SCL: OB, FB, FC, DB, UDT in SCL syntax; default), 'awl' (STL), 'db' (data block source), 'udt' (PLC data type, ends up under PLC data types). One call may contain several objects of the same kind. An object of the same name that already exists is REPLACED. A missing group in 'groupPath' is created. Order matters across calls: write a UDT before the FB that uses it, an FB before the OB or instance DB that calls it. After a compile with errors, read 'Messages', fix the text and call again with the corrected full text. LAD and FBD blocks (also replacing OB1 'Main'): use 'xml' with a SimaticML document - 'GetXmlBlock' of an existing block is the best template. TIA Portal V20: <Engineering version=\"V20\" />, interface namespace http://www.siemens.com/automation/Openness/SW/Interface/v5, network namespace http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v4. Rules: ID attributes unique per document, UId unique per network; FBD has no <Powerrail>; operands as <Access Scope=\"GlobalVariable\"><Symbol><Component Name=\"Tag\" /></Symbol></Access>, constants as LiteralConstant (with ConstantType) or GlobalConstant; logic as <Part Name=\"A\"|\"O\"|\"Coil\"|\"Sr\"|\"Ge\" ...> with TemplateValue Card/SrcType, negated inputs as <Negated Name=\"in2\" />; block calls as <Call><CallInfo Name=\"FB_x\" BlockType=\"FB\"> with an <Instance> (instance DB must exist) and one <Parameter> per interface member. Unconnected EN/output pins of calls in FBD networks are completed automatically ('Notes' says so). The first call of a server session asks the user to confirm in a dialog of the client (tick 'Yes, import'); do not answer for the user. Never save on your own: the change is only in memory until the user explicitly says 'save project'")]
        public static async Task<ResponseWrittenSource> WriteBlockSource(
            SdkMcpServer server,
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath,
            [Description("groupPath: root-relative group below 'Program blocks' (below 'PLC data types' for format 'udt'), e.g. 'Motors/Conveyor'; created when missing; empty uses the default location")] string groupPath,
            [Description("sourceText: the complete content of the source file, for example an SCL FUNCTION_BLOCK, or a SimaticML document for format 'xml'")] string sourceText,
            [Description("format: 'scl' (default), 'awl', 'db', 'udt' or 'xml' (SimaticML for LAD/FBD blocks)")] string format = "scl",
            [Description("compile: compile the generated blocks and return the compiler messages (default true)")] bool compile = true,
            [Description("keepOnError: keep objects of the text that were generated even when others in it fail. Default false: the whole text is rolled back on any error")] bool keepOnError = false,
            CancellationToken cancellationToken = default)
        {
            WritePolicy.EnsureEnabled(nameof(WriteBlockSource));

            await EnsureWriteSourceTrustedAsync(server, softwarePath, groupPath, format, sourceText, cancellationToken).ConfigureAwait(false);

            var notes = new List<string>();
            var generated = Guarded(nameof(WriteBlockSource), () => Portal.WriteSourceText(softwarePath, groupPath, sourceText, format, keepOnError, notes));

            var response = new ResponseWrittenSource
            {
                Items = generated.Select(g => new ResponseImportedProgramItem
                {
                    Name = g.Name,
                    Path = g.Path,
                    Kind = g.Kind,
                    Format = string.Equals(format, "xml", StringComparison.OrdinalIgnoreCase) ? "xml" : "source"
                }).ToList(),
                Notes = notes
            };

            // Types are compiled too: a UDT with an unknown member type used to come back as
            // 'compiled: false' without any message and only failed later in the FB that uses it.
            var compilable = generated.Where(g => g.Kind == "block" || g.Kind == "type").ToList();

            if (compile && compilable.Count > 0)
            {
                CompileGenerated(softwarePath, compilable, response);
            }

            var pending = $"{generated.Count} object(s) written to '{softwarePath}'. The change is only in memory. Do not save on your own: call '{(Portal.IsLocalSession ? "SaveSession" : "SaveProject")}' only after the user explicitly says 'save project'.";

            response.Message = response.Compiled && response.ErrorCount > 0
                ? $"{pending} Compiling reported {response.ErrorCount} error(s): read 'Messages', fix the source text and call 'WriteBlockSource' again with the corrected full text. Show the 'Report' to the user as tables, unchanged."
                : response.Compiled
                    ? $"{pending} Compiled with state '{response.State}': {response.WarningCount} warning(s). Show the 'Report' to the user as tables, unchanged."
                    : pending;

            response.Meta = new JsonObject
            {
                ["timestamp"] = DateTime.Now,
                ["success"] = true,
                ["pendingSave"] = true,
                ["writtenCount"] = generated.Count,
                ["compiled"] = response.Compiled,
                ["errorCount"] = response.ErrorCount,
                ["warningCount"] = response.WarningCount
            };

            return response;
        }

        /// <summary>
        /// Compiles each generated block and folds the results into one response. A compile
        /// error does not fail the call: the block exists, and the messages tell the agent what to fix.
        /// </summary>
        private static void CompileGenerated(string softwarePath, List<ImportedSource> blocks, ResponseWrittenSource response)
        {
            var worst = "Success";

            foreach (var block in blocks)
            {
                try
                {
                    var result = block.Kind == "type"
                        ? Portal.CompileType(softwarePath, block.Path)
                        : Portal.CompileCodeBlock(softwarePath, block.Path);

                    if (result == null)
                    {
                        response.Messages.Add(new CompileMessage { Path = block.Path, State = "Error", Description = "Compiling returned no result." });
                        response.ErrorCount++;
                        worst = "Error";
                        continue;
                    }

                    response.Messages.AddRange(FlattenCompilerMessages(result.Messages, 0));
                    response.ErrorCount += result.ErrorCount;
                    response.WarningCount += result.WarningCount;
                    worst = Worse(worst, result.State.ToString());
                }
                catch (Exception ex)
                {
                    response.Messages.Add(new CompileMessage { Path = block.Path, State = "Error", Description = $"Compiling failed: {ex.Message}" });
                    response.ErrorCount++;
                    worst = "Error";
                }
            }

            response.Compiled = true;
            response.State = worst;
            response.Report = ReportFormatter.Compile(string.Join(", ", blocks.Select(b => b.Path)), worst, response.ErrorCount, response.WarningCount, response.Messages);
        }

        private static string Worse(string a, string b)
        {
            string[] order = { "Success", "Information", "Warning", "Error" };

            return Array.IndexOf(order, b) > Array.IndexOf(order, a) ? b : a;
        }

        /// <summary>
        /// Asks the user once per server session before the agent may write source text. Later
        /// calls pass without a dialog. Refused, with the reason, when the client cannot ask and the
        /// server was not started with '--trust-writes'.
        /// </summary>
        private static async Task EnsureWriteSourceTrustedAsync(
            SdkMcpServer server, string softwarePath, string groupPath, string format, string sourceText, CancellationToken cancellationToken)
        {
            lock (TrustLock)
            {
                if (_writeSourceTrusted)
                {
                    return;
                }

                if (CliOptions.Current.TrustWrites)
                {
                    _writeSourceTrusted = true;
                    return;
                }
            }

            if (server.ClientCapabilities?.Elicitation == null)
            {
                throw new McpException(
                    "'WriteBlockSource' is refused: this client cannot show the confirmation dialog in which the user ticks " +
                    $"'{ImportConfirmation}' (MCP elicitation). Use a client with confirmation dialogs, or ask the person who configures the " +
                    "server to start it with '--trust-writes'.");
            }

            var preview = (sourceText ?? string.Empty).Split('\n').Take(12).Select(l => l.TrimEnd('\r'));

            var result = await server.ElicitAsync(new ElicitRequestParams
            {
                Message =
                    $"Allow the agent to write blocks and PLC data types directly into '{softwarePath}' of the open TIA Portal project for the rest of this session? " +
                    $"Objects of the same name are replaced. Nothing is saved to disk until you say so.{Environment.NewLine}{Environment.NewLine}" +
                    $"First write: format '{format}', group '{(string.IsNullOrWhiteSpace(groupPath) ? "(default)" : groupPath)}', beginning of the source:{Environment.NewLine}" +
                    string.Join(Environment.NewLine, preview),
                RequestedSchema = new ElicitRequestParams.RequestSchema
                {
                    Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                    {
                        ["confirmation"] = new ElicitRequestParams.UntitledMultiSelectEnumSchema
                        {
                            Title = "Confirmation",
                            MinItems = 1,
                            Items = new ElicitRequestParams.UntitledEnumItemsSchema
                            {
                                Enum = new List<string> { ImportConfirmation }
                            }
                        }
                    },
                    Required = new List<string> { "confirmation" }
                }
            }, cancellationToken).ConfigureAwait(false);

            if (!IsImportAccepted(result))
            {
                throw new McpException($"Nothing was written: the user did not tick '{ImportConfirmation}' and submit. Do not ask again unless the user asks for it.");
            }

            lock (TrustLock)
            {
                _writeSourceTrusted = true;
            }
        }

        #endregion
    }
}
