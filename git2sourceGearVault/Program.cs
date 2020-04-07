using System;
using System.IO;
using ManyConsole;

namespace git2sourceGearVault
{
    public abstract class VaultParams : ConsoleCommand
    {
        public string VaultServerIp { get; private set; }
        public string VaultRepository { get; private set; }
        public string VaultRepoFolderPath { get; private set; }
        public string VaultUser { get; private set; }
        public string VaultPassword { get; private set; }
        public string WorkDir { get; private set; }
        public string Label { get; private set; }

        protected VaultParams(string commandName, string description)
        {
            IsCommand(commandName, description);
            HasRequiredOption(nameof(VaultServerIp) + '=', "", v => VaultServerIp = v);
            HasRequiredOption(nameof(VaultRepository) + '=', "", v => VaultRepository = v);
            HasRequiredOption(nameof(VaultRepoFolderPath) + '=', "", v => VaultRepoFolderPath = v);
            HasRequiredOption(nameof(VaultUser) + '=', "", v => VaultUser = v);
            HasRequiredOption(nameof(VaultPassword) + '=', "", v => VaultPassword = v);
            HasRequiredOption(nameof(WorkDir) + '=', "Working directory where this app puts its files. Expected to be empty", v => WorkDir = v);
            HasOption(nameof(Label) + '=', "Label to apply after committing changes", v => Label = v);
        }

        protected void AssertWorkDirIsInGoodState()
        {
            Directory.CreateDirectory(WorkDir);
            if (Directory.GetFiles(WorkDir).Length + Directory.GetDirectories(WorkDir).Length > 0)
            {
                throw new Exception($"{WorkDir} is not empty");
            }
        }
    }
    
    public class GitCommand : VaultParams
    {
        public string GitUrl { get; private set; }
        public string GitBranch { get; private set; }
        
        public GitCommand() : base("git", "source data from Git")
        {
            HasRequiredOption(nameof(GitUrl) + '=', "url to git repo", v => GitUrl = v);
            HasRequiredOption(nameof(GitBranch) + '=', "Git branch", v => GitBranch = v);
        }
        
        public override int Run(string[] remainingArguments)
        {
            if (remainingArguments.Length > 0) throw new Exception($"Unknown arguments: {string.Join(", ", remainingArguments)}");
            AssertWorkDirIsInGoodState();
            return Program.GitCommand(this);
        }
    }
    
    public class FileSystemCommand : VaultParams
    {
        public string FsPath { get; private set; }
        
        public FileSystemCommand() : base("fileSystem", "source data from file system")
        {
            HasRequiredOption("sourceFrom=", "Path to folder to copy to Vault", v => FsPath = v);
        }
        
        public override int Run(string[] remainingArguments)
        {
            if (remainingArguments.Length > 0) throw new Exception($"Unknown arguments: {string.Join(", ", remainingArguments)}");
            AssertWorkDirIsInGoodState();
            return Program.FsCommand(this);
        }
    }
    
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                return ConsoleCommandDispatcher.DispatchCommand(new ConsoleCommand[]{new GitCommand(), new FileSystemCommand(), }, args, Console.Out);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.ToString());
                return -1;
            }
        }

        public static int GitCommand(GitCommand options)
        {
            var gitWorkingFolder = Path.Combine(options.WorkDir, "git");
            GitGetter.Get(options.GitUrl, options.GitBranch, gitWorkingFolder);
            return CopyProcessAndCommitChanges(options, gitWorkingFolder);
        }

        public static int FsCommand(FileSystemCommand options) => CopyProcessAndCommitChanges(options, options.FsPath);

        private static int CopyProcessAndCommitChanges(VaultParams options, string sourcePath)
        {
            var vaultWorkingDir = Path.Combine(options.WorkDir, "vault");
            using var vaultConnection = new VaultConnection(options.VaultServerIp, options.VaultRepoFolderPath, vaultWorkingDir, options.VaultRepository, options.VaultUser, options.VaultPassword);
            
            vaultConnection.Get();
            DeleteAllFilesAndSubdirs(vaultWorkingDir);
            FileSystemGetter.Get(sourcePath, vaultWorkingDir, ".git", ".idea", ".vs");
            var committedCount = vaultConnection.Commit();

            if (committedCount > 0 && !string.IsNullOrEmpty(options.Label))
            {
                vaultConnection.Label(options.Label);
            }

            return 0;
        }

        private static void DeleteAllFilesAndSubdirs(string folderPath)
        {
            var directories = Directory.GetDirectories(folderPath);
            foreach (var directory in directories)
            {
                Directory.Delete(directory, true);
            }
            
            var files = Directory.GetFiles(folderPath);
            foreach (var file in files)
            {
                File.Delete(file);
            }
        }
    }
}