using System.Linq;

namespace TiaMcpServer.Test
{
    internal static class Settings
    {

        // MultiUser Project 'TestProject1_V21'
        public const string Session1ProjectPath = "D:\\Siemens\\Sessions\\TestProject1_V21_LS_1\\TestProject1_V21_LS_1.als21";
        public const string Session1PlcSoftwarePath = "PLC_1";
        public const string Session1ExportPath = "D:\\Temp\\TIA-Portal\\Sessions\\TestProject1_V21_LS_1";
        public const string Session1PathNew = "D:\\Siemens\\Archives\\Sessions\\SaveAs\\TestProject1_V21_LS_1";

        // Local Project 'TestProject1_V21'
        public const string Project1ProjectPath = "D:\\Siemens\\Projects\\TestProject1_V21\\TestProject1_V21.ap21";
        public const string Project1PathNew = "D:\\Siemens\\Archives\\Projects\\SaveAs\\TestProject1_V21";
        public const string Project1PlcSoftwarePath0 = "PLC_0";
        public const string Project1PlcSoftwarePath1 = "PC-System_0/Software PLC_0";
        public const string Project1PlcSoftwarePath2 = "Group1/PLC_1";
        public const string Project1PlcSoftwarePath3 = "Group1/PC-System_1/Software PLC_1";
        public const string Project1PlcSoftwarePath4 = "Group1/Group1.1/PLC_1.1";
        public const string Project1PlcSoftwarePath5 = "Group1/Group1.1/PC-System_1.1/Software PLC_1.1";
        public const string Project1PlcSoftwarePath6 = "Group1/Group1.1/Group1.1.1/PLC_1.1.1";
        public const string Project1PlcSoftwarePath7 = "Group1/Group1.1/Group1.1.1/PC-System_1.1.1/Software PLC_1.1.1";
        public const string Project1ExportPath0 = "D:\\Temp\\TIA-Portal\\Projects\\Project0";
        public const string Project1ExportPath1 = "D:\\Temp\\TIA-Portal\\Projects\\Project1";
        public const string Project1ExportPath2 = "D:\\Temp\\TIA-Portal\\Projects\\Project2";
        public const string Project1ExportPath3 = "D:\\Temp\\TIA-Portal\\Projects\\Project3";
        public const string Project1ExportPath4 = "D:\\Temp\\TIA-Portal\\Projects\\Project4";
        public const string Project1ExportPath5 = "D:\\Temp\\TIA-Portal\\Projects\\Project5";
        public const string Project1ExportPath6 = "D:\\Temp\\TIA-Portal\\Projects\\Project6";
        public const string Project1ExportPath7 = "D:\\Temp\\TIA-Portal\\Projects\\Project7";

        // The newest TIA Portal installed on this machine (21 when none is found), so the suite runs
        // against V20 as well as V21 without editing this file. Read from the registry only; no
        // Siemens.Engineering type is touched before Openness.Initialize has run (AssemblyHooks).
        public static readonly int TiaMajorVersion =
            TiaMcpServer.Siemens.Diagnostics.GetInstalledTiaPortalVersions()
                .Select(i => (int?)i.MajorVersion)
                .Max() ?? 21;
    }
}
