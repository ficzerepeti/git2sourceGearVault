﻿using System;
 using System.Collections.Generic;
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

        public VaultConnection(string vaultServerIp, string repoFolder, string workingFolder)
        {
            _vaultServer = vaultServerIp;
            _repoFolder = repoFolder;
            _workingFolder = workingFolder;

            LoginAndClearChangeSet();
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
            var listFolderResult = ServerOperations.ProcessCommandListFolder(_repoFolder, true);
            
            var diskPaths = listFolderResult.Files.Cast<VaultClientFile>().Select(x => x.FullPath).ToList();
            diskPaths.AddRange(listFolderResult.Folders.Cast<VaultClientFolder>().Select(x => x.FullPath));
            
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

        public void Commit()
        {
            var changes = RemoveDuplicatesFromChangeSet(_changes);
            ServerOperations.ProcessCommandCommit(changes, UnchangedHandler.Checkin, false, LocalCopyType.Leave, false);
        }

        public void Dispose()
        {
            ServerOperations.ProcessCommandUndoCheckout(new []{_repoFolder}, true, LocalCopyType.Leave);
            ServerOperations.Logout();
        }

        public void PrintChangeSet() => Console.WriteLine($"[{nameof(PrintChangeSet)}] Change set item count: {_changes.Count}. {string.Join(", ", _changes.Cast<ChangeSetItem>().Select(csi => $"{csi.DisplayRepositoryPath}-{csi.Type}"))}");
        public void PrintChangeSetWithoutDuplicates()
        {
            var changes = RemoveDuplicatesFromChangeSet(_changes);
            Console.WriteLine($"[{nameof(PrintChangeSetWithoutDuplicates)}] Change set item count: {changes.Count}. {string.Join(", ", changes.Cast<ChangeSetItem>().Select(csi => $"{csi.DisplayRepositoryPath}-{csi.Type}"))}");
        }

        private void LoginAndClearChangeSet()
        {
            Console.WriteLine($"About to log in to {_vaultServer}");
            ServerOperations.SetLoginOptions($"http://{_vaultServer}/VaultService", "TestGit2SourceGear", "123456", "Hardcastle Source", false);
            ServerOperations.Login();
            Console.WriteLine("Connected");
                
            ServerOperations.client.MakeBackups = false;
            ServerOperations.client.AutoCommit = false;
            ServerOperations.client.Verbose = true;
                
            Console.WriteLine($"Setting working folder to {_workingFolder}");
            ServerOperations.SetWorkingFolder(_repoFolder, _workingFolder, true);

            Console.WriteLine($"{nameof(ServerOperations.isConnected)}: {ServerOperations.isConnected()}");

            var lcs = ServerOperations.ProcessCommandListChangeSet(new []{_repoFolder});
            if (lcs.Count <= 0) return;
            
            Console.WriteLine($"Removing {lcs.Count} item(s) from change set");
            foreach (var _ in lcs)
            {
                ServerOperations.ProcessCommandUndoChangeSetItem(0);
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
                    continue;
                }

                dict[item.DisplayRepositoryPath] = item.Type;
                result.Add(item);
            }

            return result;
        }
    }
}