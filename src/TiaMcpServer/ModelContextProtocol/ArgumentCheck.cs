using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// Tells the calling agent what was wrong with a tool call instead of the SDK's bare
    /// "An error occurred invoking 'X'.".
    ///
    /// The SDK keeps the text of an exception only when it is an McpException; everything else,
    /// including the ArgumentException of the parameter binding for a missing required argument,
    /// becomes that generic sentence, and the reason ends up only in the server log. An argument
    /// with a wrong name is not even an error there: the binder ignores it and the tool runs with
    /// the default value (a 'regex' instead of 'regexName' would push every changed file).
    ///
    /// Two parts, both in the outermost call-tool filter:
    /// 1. Before the call: the arguments are checked against the tool's input schema as the client
    ///    sees it. Unknown names and missing required ones are reported together with the list of
    ///    parameters the tool takes. Names the server fills from its presets ('softwarePath', ...)
    ///    and 'portalId' count as known, because agents send them anyway.
    /// 2. Around the call: any exception that is not an McpException is passed on as one, so the
    ///    SDK keeps its text (type and message), e.g. a value of the wrong type.
    ///
    /// Callers: Program.cs (Apply for each registered tool, after ToolDefaults and PortalSelection;
    /// Filter as the first call-tool filter). Affected API: no tool or schema changes; calls with
    /// unknown or missing arguments now fail with a message instead of running or failing silently.
    /// Reads/writes no data files.
    /// </summary>
    public static class ArgumentCheck
    {
        /// <summary>What the check knows about one tool: the schema it offers and the names it accepts.</summary>
        public sealed class ToolArguments
        {
            /// <summary>Parameters in the input schema, in schema order, with their 'required' flag.</summary>
            public List<(string Name, bool Required)> Offered { get; } = new List<(string Name, bool Required)>();

            /// <summary>Every name the call may contain: the schema plus the preset names the server fills itself.</summary>
            public HashSet<string> Accepted { get; } = new HashSet<string>(StringComparer.Ordinal);
        }

        private static readonly Dictionary<string, ToolArguments> _tools = new(StringComparer.Ordinal);

        /// <summary>Records the final input schema of a tool; call it after every other schema change.</summary>
        public static McpServerTool Apply(MethodInfo method, McpServerTool tool)
        {
            var arguments = Read(tool.ProtocolTool.InputSchema);

            // Preset arguments are removed from the schema (ToolDefaults) but still accepted.
            foreach (var parameter in method.GetParameters())
            {
                if (parameter.Name != null && ToolDefaults.Presets.ContainsKey(parameter.Name))
                {
                    arguments.Accepted.Add(parameter.Name);
                }
            }

            arguments.Accepted.Add(PortalSelection.ArgumentName);

            lock (_tools)
            {
                _tools[tool.ProtocolTool.Name] = arguments;
            }

            return tool;
        }

        /// <summary>What <see cref="Apply"/> recorded for a tool, or null for a tool it never saw.</summary>
        public static ToolArguments? Registered(string? toolName)
        {
            if (toolName == null)
            {
                return null;
            }

            lock (_tools)
            {
                return _tools.TryGetValue(toolName, out var tool) ? tool : null;
            }
        }

        /// <summary>The parameters of an input schema ('properties' and 'required').</summary>
        public static ToolArguments Read(JsonElement schema)
        {
            var result = new ToolArguments();
            var required = new HashSet<string>(StringComparer.Ordinal);

            if (schema.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            if (schema.TryGetProperty("required", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String))
                {
                    required.Add(item.GetString()!);
                }
            }

            if (schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in properties.EnumerateObject())
                {
                    result.Offered.Add((property.Name, required.Contains(property.Name)));
                    result.Accepted.Add(property.Name);
                }
            }

            return result;
        }

        /// <summary>
        /// The message for a call whose arguments do not fit the tool, or null when they fit.
        /// A required argument sent as null counts as missing.
        /// </summary>
        public static string? Problem(string toolName, ToolArguments tool, IDictionary<string, JsonElement>? arguments)
        {
            arguments ??= new Dictionary<string, JsonElement>();

            var unknown = arguments.Keys.Where(k => !tool.Accepted.Contains(k)).ToList();
            var missing = tool.Offered
                .Where(p => p.Required)
                .Where(p => !arguments.TryGetValue(p.Name, out var value) || value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined)
                .Select(p => p.Name)
                .ToList();

            if (unknown.Count == 0 && missing.Count == 0)
            {
                return null;
            }

            var parts = new List<string>();

            if (unknown.Count > 0)
            {
                var hints = unknown.Select(u => Closest(u, tool.Offered.Select(p => p.Name)) is string near ? $"'{u}' (did you mean '{near}'?)" : $"'{u}'");
                parts.Add($"unknown parameter(s) {string.Join(", ", hints)}");
            }

            if (missing.Count > 0)
            {
                parts.Add($"missing required parameter(s) {string.Join(", ", missing.Select(m => $"'{m}'"))}");
            }

            var offered = tool.Offered.Count == 0
                ? "none"
                : string.Join(", ", tool.Offered.Select(p => p.Required ? $"{p.Name} (required)" : p.Name));

            return $"'{toolName}' was not called: {string.Join("; ", parts)}. Its parameters are: {offered}. Call it again with these names.";
        }

        /// <summary>The offered name that differs from <paramref name="name"/> only in case, or contains it, or is contained in it.</summary>
        private static string? Closest(string name, IEnumerable<string> offered)
        {
            var list = offered.ToList();

            return list.FirstOrDefault(o => string.Equals(o, name, StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(o => o.Length >= 4 && name.Length >= 4 &&
                    (o.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf(o, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        /// <summary>
        /// True for exceptions the SDK must see unchanged: its own (they already carry their text,
        /// and protocol errors become JSON-RPC errors), a cancellation, and a request for input.
        /// </summary>
        public static bool PassesThrough(Exception exception)
        {
            return exception is McpException
                || exception is OperationCanceledException
                || exception is InputRequiredException;
        }

        /// <summary>The text an exception gets for the caller: its type and every distinct message down the inner exceptions.</summary>
        public static string Describe(Exception exception)
        {
            var messages = new List<string>();

            for (var current = exception; current != null; current = current.InnerException)
            {
                var message = current.Message?.Trim();

                if (!string.IsNullOrEmpty(message) && !messages.Contains(message!))
                {
                    messages.Add(message!);
                }
            }

            return $"{exception.GetType().Name}: {string.Join(": ", messages)}";
        }

        public static McpRequestFilter<CallToolRequestParams, CallToolResult> Filter =>
            next => async (context, cancellationToken) =>
            {
                var name = context.Params?.Name;
                var tool = Registered(name);

                // An unknown tool is the SDK's business ("Unknown tool").
                if (tool != null)
                {
                    var problem = Problem(name!, tool, context.Params!.Arguments);

                    if (problem != null)
                    {
                        return new CallToolResult
                        {
                            IsError = true,
                            Content = [new TextContentBlock { Text = problem }]
                        };
                    }
                }

                try
                {
                    return await next(context, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!PassesThrough(ex))
                {
                    throw new McpException(Describe(ex), ex);
                }
            };
    }
}
