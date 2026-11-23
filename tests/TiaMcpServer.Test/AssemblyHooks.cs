using System;

namespace TiaMcpServer.Test
{
    [TestClass]
    public class AssemblyHooks
    {
        [AssemblyInitialize]
        public static void AssemblyInit(TestContext context)
        {
            // Runs once before any tests in the assembly.
            // The removed Siemens.Collaboration resolver package used to load Siemens.Engineering.dll
            // on its own; without it, the resolver must be registered before the first test touches
            // a Siemens type (e.g. ProgrammingLanguage in the export format tests).
            TiaMcpServer.Siemens.Openness.Initialize(Settings.TiaMajorVersion);
        }

        [AssemblyCleanup]
        public static void AssemblyCleanup()
        {
            // Runs once after all tests in the assembly  
            // Console.WriteLine("Assembly cleanup completed");
        }
    }
}
