using System;
using System.IO;
using VaultClientIntegrationLib;

namespace git2sourceGearVault
{
    
    internal static class Program
    {
        private const string VaultServer = "10.1.10.41";
        private const string RepoFolderPath = "$/Misc/Git2SourceGear";
        private static readonly string TempWorkingFolder = Path.Combine(Path.GetTempPath(), "git2sourceGear_working_folder");

        public static void Main()
        {
            try
            {
                using (var vaultConnection = new VaultConnection(VaultServer, RepoFolderPath, TempWorkingFolder))
                {
                    ServerOperations.GetInstance().ChangesetOutput += csic => Console.WriteLine($"[ChangesetOutput] Change set count: {csic.Count}. {string.Join(", ", csic)}\n");
                
                    vaultConnection.Get();
                    Console.WriteLine();
                
                    Directory.CreateDirectory(TempWorkingFolder);

                    vaultConnection.DetectModifiedAndDeletedFiles();
                    vaultConnection.PrintChangeSet();
                    Console.WriteLine();

                    const string folderName = "another_folder";
                    Directory.CreateDirectory(Path.Combine(TempWorkingFolder, folderName));

                    CreateAndAddFile(vaultConnection, "just_a_folder", "another_file1.txt", "111111111111");
                    CreateAndAddFile(vaultConnection, folderName, "another_file1.txt", "111111111111");
                    CreateAndAddFile(vaultConnection, folderName, "another_file2.txt", "222222222222");

                    vaultConnection.Commit();
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.ToString());
            }
        }

        private static void CreateAndAddFile(VaultConnection vaultConnection, string folderName, string fileName, string content)
        {
            var fileDiskPath = Path.Combine(TempWorkingFolder, folderName, fileName);
            File.WriteAllText(fileDiskPath, content);
        
            Console.WriteLine($"Adding {fileDiskPath}");
            vaultConnection.AddFile($"{RepoFolderPath}/{folderName}", fileDiskPath);
            vaultConnection.PrintChangeSet();
            Console.WriteLine();
            vaultConnection.PrintChangeSetWithoutDuplicates();
            Console.WriteLine();
            Console.WriteLine();
        }
    }
}