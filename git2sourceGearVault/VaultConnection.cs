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
        
        private readonly ChangeSetItemColl _changes = new ChangeSetItemColl();
        private readonly HashSet<string> _repoDirsToDelete = new HashSet<string>();

        public VaultConnection(string vaultServerIp, string repoFolder, string workingFolder)
        {
            _vaultServer = vaultServerIp;
            _repoFolder = repoFolder;
            _workingFolder = workingFolder;

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
        
        public void AddFile(string repoPath, params string[] diskPaths) => _changes.AddRange(ServerOperations.ProcessCommandAdd(repoPath, diskPaths));

        public void DetectModifiedAndDeletedFiles()
        {
            _repoDirsToDelete.Clear();
            var folder = ServerOperations.ProcessCommandListFolder(_repoFolder, true);
            HandleFolderChangesRecursively(folder);
        }

        public void Commit()
        {
            var changes = RemoveDuplicatesFromChangeSet(_changes);
            ServerOperations.ProcessCommandCommit(changes, UnchangedHandler.Checkin, false, LocalCopyType.Leave, false);
            _changes.Clear();
        }

        public void Dispose()
        {
            ServerOperations.ProcessCommandUndoCheckout(new []{_repoFolder}, true, LocalCopyType.Leave);
            ClearChangeSet();
            ServerOperations.RemoveWorkingFolder(_repoFolder);
            ServerOperations.Logout();
        }

        public void PrintChangeSet() => Console.WriteLine($"[{nameof(PrintChangeSet)}] Change set item count: {_changes.Count}. {string.Join(", ", _changes.Cast<ChangeSetItem>().Select(csi => $"{csi.DisplayRepositoryPath}-{csi.Type}"))}");
        public void PrintChangeSetWithoutDuplicates()
        {
            var changes = RemoveDuplicatesFromChangeSet(_changes);
            Console.WriteLine($"[{nameof(PrintChangeSetWithoutDuplicates)}] Change set item count: {changes.Count}. {string.Join(", ", changes.Cast<ChangeSetItem>().Select(csi => $"{csi.DisplayRepositoryPath}-{csi.Type}"))}");
        }

        private void Login()
        {
            Console.WriteLine($"About to log in to {_vaultServer}");
            ServerOperations.SetLoginOptions($"http://{_vaultServer}/VaultService", "TestGit2SourceGear", "123456", "Hardcastle Source", false);
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

            Console.WriteLine($"Removing {lcs.Count} item(s) from change set");
            foreach (var _ in lcs)
            {
                ServerOperations.ProcessCommandUndoChangeSetItem(0);
            }
        }
        
        private void HandleFolderChangesRecursively(VaultClientFolder folder)
        {
            HandleFiles(folder.Files);
            foreach (VaultClientFolder subFolder in folder.Folders)
            {
                HandleFolderChangesRecursively(subFolder);
            }

            if (string.Equals(_repoFolder, folder.FullPath, StringComparison.InvariantCultureIgnoreCase))
            {
                return;
            }

            var diskPath = MakeDiskPath(folder.FullPath);
            if (!Directory.Exists(diskPath))
            {
                _repoDirsToDelete.Add(folder.FullPath);
            }
        }

        private void HandleFiles(VaultClientFileColl fileColl)
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
                        _changes.AddRange(ServerOperations.ProcessCommandDelete(new[] {path}));
                        Console.WriteLine($"Deleted {path} as it was {status}");
                        break;
                    
                    case WorkingFolderFileStatus.Renegade:
                        ServerOperations.ProcessCommandCheckout(new[] {path}, false, false, new GetOptions());
                        _changes.AddRange(ServerOperations.ProcessCommandListChangeSet(new[] {path}));
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

        private ChangeSetItemColl RemoveDuplicatesFromChangeSet(ChangeSetItemColl input)
        {
            var result = new ChangeSetItemColl();
            var dict = new Dictionary<string, ChangeSetItemType>();
            foreach (ChangeSetItem item in input)
            {
                if (dict.TryGetValue(item.DisplayRepositoryPath, out var status))
                {
                    if (status != item.Type) throw new Exception($"{item.DisplayRepositoryPath} has multiple statuses: {status} and {item.Type}");
                    continue;
                }

                if (_repoDirsToDelete.Any(x => item.DisplayRepositoryPath.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"Removing {item.DisplayRepositoryPath} from change set as it is in a deleted folder");
                    continue;
                }

                dict[item.DisplayRepositoryPath] = item.Type;
                result.Add(item);
            }

            result.AddRange(ServerOperations.ProcessCommandDelete(_repoDirsToDelete.ToArray()));

            return result;
        }

        private string MakeDiskPath(string repoPath)
        {
            if (!repoPath.StartsWith(_repoFolder, StringComparison.InvariantCultureIgnoreCase)) throw new Exception($"Path {repoPath} doesn't start with {_repoFolder}");
            var suffix = repoPath.Substring(_repoFolder.Length);
            return _workingFolder + suffix;
        }
    }
}