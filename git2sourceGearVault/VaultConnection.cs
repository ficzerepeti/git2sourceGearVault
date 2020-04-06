﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VaultClientIntegrationLib;
using VaultClientOperationsLib;

namespace git2sourceGearVault
{
    public class VaultConnection : IDisposable
    {
        private readonly string _vaultServer;
        private readonly string _repoFolder;
        private readonly string _workingFolder;
        private readonly string _vaultRepository;
        private readonly string _user;
        private readonly string _password;

        public VaultConnection(string vaultServerIp, string repoFolder, string workingFolder, string vaultRepository, string user, string password)
        {
            _vaultServer = vaultServerIp;
            _repoFolder = repoFolder;
            _workingFolder = workingFolder;
            _vaultRepository = vaultRepository;
            _user = user;
            _password = password;

            Login();
            ClearChangeSet();
        }

        public void Get()
        {
            var getOptions = new GetOptions
            {
                MakeWritable = MakeWritableType.MakeAllFilesWritable, Recursive = true, Merge = MergeType.OverwriteWorkingCopy, PerformDeletions = PerformDeletionsType.RemoveWorkingCopy
            };
            Console.WriteLine("Getting from SourceGear");
            GetOperations.ProcessCommandGet(new []{_repoFolder}, getOptions);
        }

        public void Commit()
        {
            var changes = DetectChanges();
            Console.WriteLine($"[{nameof(Commit)}] Committing the following changes: {changes}");
            ServerOperations.ProcessCommandCommit(changes, UnchangedHandler.Checkin, false, LocalCopyType.Leave, false);
        }

        public void Dispose()
        {
            ServerOperations.ProcessCommandUndoCheckout(new []{_repoFolder}, true, LocalCopyType.Leave);
            ClearChangeSet();
            ServerOperations.RemoveWorkingFolder(_repoFolder);
            ServerOperations.Logout();
        }

        private void Login()
        {
            Console.WriteLine($"About to log in to {_vaultServer}");
            ServerOperations.SetLoginOptions($"http://{_vaultServer}/VaultService", _user, _password, _vaultRepository, false);
            ServerOperations.Login();
            Console.WriteLine("Connected");
                
            ServerOperations.client.MakeBackups = false;
            ServerOperations.client.AutoCommit = false;
            ServerOperations.client.Verbose = true;
                
            Console.WriteLine($"Setting working folder to {_workingFolder}");
            ServerOperations.RemoveWorkingFolder(_repoFolder);
            ServerOperations.SetWorkingFolder(_repoFolder, _workingFolder, true);

            Console.WriteLine($"{nameof(ServerOperations.isConnected)}: {ServerOperations.isConnected()}");
        }

        private void ClearChangeSet()
        {
            var lcs = ServerOperations.ProcessCommandListChangeSet(new[] {_repoFolder});
            if (lcs.Count <= 0) return;

            Console.Write($"Removing {lcs.Count} item(s) from change set");
            foreach (var _ in lcs)
            {
                ServerOperations.ProcessCommandUndoChangeSetItem(0);
                Console.Write('.');
            }
            Console.WriteLine();
        }

        private ChangeSetItemColl DetectChanges()
        {
            var changes = new ChangeSetItemColl();
            var folder = ServerOperations.ProcessCommandListFolder(_repoFolder, true);

            HandleModifiesAndDeletes(folder, changes, true);
            HandleAdds(folder, changes);

            return RemoveDuplicatesFromChangeSet(changes);
        }

        private void HandleAdds(VaultClientFolder folder, ChangeSetItemColl changes)
        {
            var versionedFiles = new HashSet<string>();
            GetAllVersionedFiles(folder, versionedFiles);
            var versionedDiskPaths = versionedFiles.Select(MakeDiskPath).ToList();

            var filesToAdd = new HashSet<string>(Directory.GetFiles(_workingFolder, "*", SearchOption.AllDirectories));
            filesToAdd.ExceptWith(versionedDiskPaths);
            
            var dirToFiles = new Dictionary<string, HashSet<string>>();
            foreach (var file in filesToAdd)
            {
                var dir = Path.GetDirectoryName(file);
                dir = dir.Substring(Math.Min(_workingFolder.Length + 1, dir.Length));
                if (!dirToFiles.TryGetValue(dir, out var files))
                {
                    files = new HashSet<string>();
                    dirToFiles[dir] = files;
                }

                files.Add(file);
            }
            Console.WriteLine($"Files to add: {string.Join(", ", filesToAdd)}");

            foreach (var pair in dirToFiles)
            {
                var dir = pair.Key;
                var files = pair.Value;

                var repoFolder = string.IsNullOrEmpty(dir) ? _repoFolder : $"{_repoFolder}/{dir}";
                changes.AddRange(ServerOperations.ProcessCommandAdd(repoFolder, files.ToArray()));
            }
        }
        
        private void HandleModifiesAndDeletes(VaultClientFolder folder, ChangeSetItemColl changes, bool isRepoRoot)
        {
            var diskPath = MakeDiskPath(folder.FullPath);
            if (!isRepoRoot && !Directory.Exists(diskPath))
            {
                changes.AddRange(ServerOperations.ProcessCommandDelete(new[] {folder.FullPath}));
                return;
            }
            
            HandleFiles(folder.Files, changes);
            foreach (VaultClientFolder subFolder in folder.Folders)
            {
                HandleModifiesAndDeletes(subFolder, changes, false);
            }
        }

        private static void HandleFiles(VaultClientFileColl fileColl, ChangeSetItemColl changes)
        {
            var diskPaths = fileColl.Cast<VaultClientFile>().Select(x => x.FullPath).ToList();
            var statuses = ServerOperations.ProcessCommandStatus(diskPaths.ToArray());

            var pathAndStatus = diskPaths.Zip(statuses, Tuple.Create);
            foreach (var (path, status) in pathAndStatus)
            {
                switch (status)
                {
                    case WorkingFolderFileStatus.None:
                    case WorkingFolderFileStatus.Edited:
                        break;
                    
                    case WorkingFolderFileStatus.Missing:
                        changes.AddRange(ServerOperations.ProcessCommandDelete(new[] {path}));
                        Console.WriteLine($"Deleted {path} as it was {status}");
                        break;
                    
                    case WorkingFolderFileStatus.Renegade:
                        ServerOperations.ProcessCommandCheckout(new[] {path}, false, false, new GetOptions());
                        changes.AddRange(ServerOperations.ProcessCommandListChangeSet(new[] {path}));
                        Console.WriteLine($"Checked out {path} as it was {status}");
                        break;
                    
                    case WorkingFolderFileStatus.Merged:
                    case WorkingFolderFileStatus.NeedsMerge:
                    case WorkingFolderFileStatus.Unknown:
                    case WorkingFolderFileStatus.MoreRecent:
                    case WorkingFolderFileStatus.Old:
                        Console.WriteLine($"Unhandled {path} with {status}");
                        break;
                        
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private static ChangeSetItemColl RemoveDuplicatesFromChangeSet(ChangeSetItemColl input)
        {
            var result = new ChangeSetItemColl();
            var dict = new Dictionary<string, ChangeSetItemType>();
            foreach (ChangeSetItem item in input)
            {
                if (dict.TryGetValue(item.DisplayRepositoryPath, out var status))
                {
                    if (status != item.Type) throw new Exception($"{item.DisplayRepositoryPath} has multiple statuses: {status} and {item.Type}");
                    Console.WriteLine($"***** ignoring duplicate {item.DisplayRepositoryPath} - {item.Type}");
                    continue;
                }

                dict[item.DisplayRepositoryPath] = item.Type;
                result.Add(item);
            }

            return result;
        }

        private static void GetAllVersionedFiles(VaultClientFolder folder, HashSet<string> versionedFiles)
        {
            foreach (VaultClientFile file in folder.Files)
            {
                versionedFiles.Add(file.FullPath);
            }

            foreach (VaultClientFolder subFolder in folder.Folders)
            {
                GetAllVersionedFiles(subFolder, versionedFiles);
            }
        }

        private string MakeDiskPath(string repoPath)
        {
            if (!repoPath.StartsWith(_repoFolder, StringComparison.InvariantCultureIgnoreCase)) throw new Exception($"Path {repoPath} doesn't start with {_repoFolder}");
            var suffix = repoPath.Substring(_repoFolder.Length);
            var result = _workingFolder + suffix;
            return result.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }
    }
}