using System;
using System.IO;
using VaultClientIntegrationLib;
using VaultClientOperationsLib;

namespace git2sourceGearVault
{
    
    internal static class Program
    {
        private const string VaultServer = "10.1.10.41";
        private const string RepoFolderPath = "$/misc/Git2SourceGear";
        private static readonly string TempWorkingFolder = Path.Combine(Path.GetTempPath(), "git2sourceGear_working_folder");

        public static void Main()
        {
            try
            {
                using (var vaultConnection = new VaultConnection(VaultServer, RepoFolderPath, TempWorkingFolder))
                {
                    try
                    {
                        vaultConnection.Get();
                        Console.WriteLine();

                        vaultConnection.DetectModifiedAndDeletedFiles();
                        vaultConnection.PrintChangeSet();
                        Console.WriteLine();
                    
                        Directory.Delete(Path.Combine(TempWorkingFolder, "just_a_folder"), true);
                        File.Delete(Path.Combine(TempWorkingFolder, "another_folder", "another_file1.txt"));
                        vaultConnection.DetectModifiedAndDeletedFiles();
                        vaultConnection.PrintChangeSet();
                        Console.WriteLine();

                        vaultConnection.Commit();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Before dispose: {e}");
                        throw;
                    }
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.ToString());
            }
        }
    }
}