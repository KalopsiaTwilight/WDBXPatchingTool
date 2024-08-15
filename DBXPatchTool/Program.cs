using DBXPatching.Core;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DBXPatchTool
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("DBXPatchTool usage: DBXPatchTool {patchFile - path to patch file to apply} {dbcDir - path to directory where dbc data is stored} {optional outputdir - path to directory where output dbc should be written");
                Environment.Exit(1);
            }
            var patchFilePath = args[0];
            if (!File.Exists(patchFilePath))
            {
                Console.WriteLine($"Could not find file in provided path: '{patchFilePath}'");
                Environment.Exit((int)PatchingResultCode.ERROR_INVALID_ARGUMENT);
            }
            Patch? patch;
            try
            {
                patch = JsonSerializer.Deserialize<Patch>(File.ReadAllText(patchFilePath));
            } catch
            {
                patch = null;
            }
            if (patch == null)
            {
                Console.WriteLine($"Failed to read file '{patchFilePath}' as a patch file.");
                Environment.Exit((int)PatchingResultCode.ERROR_INVALID_ARGUMENT);
            }
            
            var readDir = args[1];
            if (!Directory.Exists(readDir))
            {
                Console.WriteLine($"Could not find directory '{readDir}'");
                Environment.Exit((int)PatchingResultCode.ERROR_INVALID_ARGUMENT);
            }

            var outputPath = readDir;
            if (args.Length == 3)
            {
                outputPath = args[2];
                if (!Directory.Exists(outputPath))
                {
                    Directory.CreateDirectory(outputPath);
                }
            }

            var patcher = new DBXPatcher(readDir, outputPath);
            var result = patcher.ApplyPatch(patch);
            foreach(var message in result.Messages)
            {
                Console.WriteLine(message);
            }
            Environment.Exit((int)result.ResultCode);
        }
    }
}
