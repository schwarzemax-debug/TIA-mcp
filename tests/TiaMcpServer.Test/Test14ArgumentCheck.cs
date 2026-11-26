using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.Test
{
    /// <summary>
    /// Tests for the argument check in front of every tool call: the SDK answers a call with a
    /// wrong or missing argument only with "An error occurred invoking 'X'.", and ignores unknown
    /// argument names. These tests do not connect to TIA Portal.
    /// </summary>
    [TestClass]
    public class Test14ArgumentCheck
    {
        private static IDictionary<string, JsonElement> Args(string json)
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        }

        private static ArgumentCheck.ToolArguments Schema(string json)
        {
            using (var document = JsonDocument.Parse(json))
            {
                return ArgumentCheck.Read(document.RootElement.Clone());
            }
        }

        private const string WhereUsedLike =
            "{ \"type\": \"object\", \"properties\": { \"name\": { \"type\": \"string\" }, \"kind\": { \"type\": \"string\" } }, \"required\": [\"name\"] }";

        [TestMethod]
        public void Test_1400_Problem_FittingArguments_IsNull()
        {
            Assert.IsNull(ArgumentCheck.Problem("WhereUsed", Schema(WhereUsedLike), Args("{ \"name\": \"FB_X\" }")));
            Assert.IsNull(ArgumentCheck.Problem("WhereUsed", Schema(WhereUsedLike), Args("{ \"name\": \"FB_X\", \"kind\": \"block\" }")));
        }

        [TestMethod]
        public void Test_1401_Problem_UnknownAndMissing_AreNamedWithTheParameterList()
        {
            // Act: the call that failed silently in practice ('objectPath' instead of 'name')
            var problem = ArgumentCheck.Problem("WhereUsed", Schema(WhereUsedLike), Args("{ \"objectPath\": \"FB_X\" }"));

            // Assert
            Assert.IsNotNull(problem);
            StringAssert.Contains(problem, "'WhereUsed' was not called");
            StringAssert.Contains(problem, "unknown parameter(s) 'objectPath'");
            StringAssert.Contains(problem, "missing required parameter(s) 'name'");
            StringAssert.Contains(problem, "name (required), kind");
        }

        [TestMethod]
        public void Test_1402_Problem_UnknownName_GetsAHintToTheNearOne()
        {
            var schema = Schema("{ \"type\": \"object\", \"properties\": { \"regexName\": { \"type\": \"string\" } } }");

            StringAssert.Contains(ArgumentCheck.Problem("TiaPush", schema, Args("{ \"regex\": \"^Main$\" }")), "did you mean 'regexName'?");
            StringAssert.Contains(ArgumentCheck.Problem("TiaPush", schema, Args("{ \"RegexName\": \"^Main$\" }")), "did you mean 'regexName'?");
        }

        [TestMethod]
        public void Test_1403_Problem_RequiredSentAsNull_CountsAsMissing()
        {
            StringAssert.Contains(
                ArgumentCheck.Problem("WhereUsed", Schema(WhereUsedLike), Args("{ \"name\": null }")),
                "missing required parameter(s) 'name'");
        }

        [TestMethod]
        public void Test_1404_Problem_NoArgumentsAtAll_IsFineForAToolWithoutRequiredOnes()
        {
            var schema = Schema("{ \"type\": \"object\", \"properties\": { \"regexName\": { \"type\": \"string\" } } }");

            Assert.IsNull(ArgumentCheck.Problem("TiaPull", schema, null));
        }

        [TestMethod]
        public void Test_1405_RegisteredTools_AcceptPresetsAndPortalId_AndReportWrongNames()
        {
            // Arrange: 'softwarePath' is a preset, so it is not in the schema but agents still send it
            ToolDefaults.Configure(new CliOptions { SoftwarePath = "PLC_1" });

            try
            {
                var tool = Program.BuildTools(allowWrite: true).Single(t => t.ProtocolTool.Name == "WhereUsed");
                var arguments = ArgumentCheck.Read(tool.ProtocolTool.InputSchema);

                // Act / Assert: the schema as the client sees it
                Assert.IsFalse(arguments.Offered.Any(p => p.Name == "softwarePath"), "presets are not offered");
                CollectionAssert.Contains(arguments.Offered.Where(p => p.Required).Select(p => p.Name).ToList(), "name");

                // The registered check: preset and portalId pass, the wrong name does not
                var registered = RegisteredArguments("WhereUsed");

                Assert.IsNull(ArgumentCheck.Problem("WhereUsed", registered, Args("{ \"name\": \"FB_X\", \"softwarePath\": \"PLC_1\", \"portalId\": 1234 }")));
                StringAssert.Contains(ArgumentCheck.Problem("WhereUsed", registered, Args("{ \"objectPath\": \"FB_X\" }")), "missing required parameter(s) 'name'");
            }
            finally
            {
                ToolDefaults.Configure(new CliOptions());
            }
        }

        [TestMethod]
        public void Test_1406_EveryRegisteredTool_AcceptsTheArgumentsItsSchemaOffers()
        {
            foreach (var tool in Program.BuildTools(allowWrite: true))
            {
                var name = tool.ProtocolTool.Name;
                var registered = RegisteredArguments(name);

                // Act: every offered parameter with a value
                var all = registered.Offered.ToDictionary(p => p.Name, p => JsonDocument.Parse("\"x\"").RootElement.Clone());

                // Assert: the check never rejects a name the tool itself declares
                Assert.IsNull(ArgumentCheck.Problem(name, registered, all), name);
            }
        }

        [TestMethod]
        public void Test_1407_PassesThrough_OnlyTheSdkOwnAndCancellation()
        {
            Assert.IsTrue(ArgumentCheck.PassesThrough(new McpException("x")));
            Assert.IsTrue(ArgumentCheck.PassesThrough(new McpProtocolException("x")));
            Assert.IsTrue(ArgumentCheck.PassesThrough(new OperationCanceledException()));
            Assert.IsFalse(ArgumentCheck.PassesThrough(new ArgumentException("x")));
            Assert.IsFalse(ArgumentCheck.PassesThrough(new InvalidOperationException("x")));
        }

        [TestMethod]
        public void Test_1408_Describe_NamesTheTypeAndEveryMessage()
        {
            var exception = new ArgumentException("The arguments dictionary is missing a value for the required parameter 'name'.",
                new FormatException("inner reason"));

            var text = ArgumentCheck.Describe(exception);

            Assert.AreEqual("ArgumentException: The arguments dictionary is missing a value for the required parameter 'name'.: inner reason", text);
        }

        /// <summary>What ArgumentCheck.Apply recorded for a tool (BuildTools runs it for every tool).</summary>
        private static ArgumentCheck.ToolArguments RegisteredArguments(string toolName)
        {
            return ArgumentCheck.Registered(toolName) ?? throw new AssertFailedException($"'{toolName}' was not registered");
        }
    }
}
