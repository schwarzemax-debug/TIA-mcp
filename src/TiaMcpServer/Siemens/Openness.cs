using System;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// TIA Openness bootstrap without the Siemens.Collaboration.Net NuGet packages.
    /// Those packages ship unsigned DLLs that Windows Smart App Control / WDAC block
    /// (FileLoadException 0x800711C7), which took the whole server down at start-up.
    /// Siemens.Engineering.dll itself is resolved by <see cref="Engineering.Resolver"/> from
    /// the TIA Portal installation (registry, fallback: TiaPortalLocation).
    /// </summary>
    public static class Openness
    {
        public const string OpennessGroupName = "Siemens TIA Openness";

        public static int TiaMajorVersion { get; private set; }

        public static void Initialize(int? tiaMajorVersion = 21)
        {
            TiaMajorVersion = tiaMajorVersion ?? 21;
            Engineering.TiaMajorVersion = TiaMajorVersion;

            // Must be registered before the first type from Siemens.Engineering is touched.
            AppDomain.CurrentDomain.AssemblyResolve -= Engineering.Resolver;
            AppDomain.CurrentDomain.AssemblyResolve += Engineering.Resolver;
        }

        /// <summary>
        /// Read-only check whether the current user is a member of the 'Siemens TIA Openness' group.
        /// Uses the token groups of the current process, so a membership only becomes visible after
        /// a new logon (same behaviour as TIA Portal itself).
        /// </summary>
        public static bool CheckUserInGroup()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                if (identity.Groups == null)
                {
                    return false;
                }

                foreach (var sid in identity.Groups)
                {
                    try
                    {
                        var account = sid.Translate(typeof(NTAccount)).Value;
                        var name = account.Split('\\').Last();
                        if (string.Equals(name, OpennessGroupName, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch (IdentityNotMappedException)
                    {
                        // orphaned SID - ignore
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TiaMcpServer] Could not check the '{OpennessGroupName}' membership: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Never changes the group membership (that is an administrator's decision). Reports on stderr
        /// how to fix it instead.
        /// </summary>
        public static Task<bool> IsUserInGroup()
        {
            if (CheckUserInGroup())
            {
                return Task.FromResult(true);
            }

            Console.Error.WriteLine(
                $"[TiaMcpServer] The current Windows user is not a member of the local group '{OpennessGroupName}'. " +
                "Add the user to that group (Computer Management > Local Users and Groups), log off and on again, then restart the server.");
            return Task.FromResult(false);
        }
    }
}
