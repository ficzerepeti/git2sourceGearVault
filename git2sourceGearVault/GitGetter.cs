using System;
using LibGit2Sharp;

namespace git2sourceGearVault
{
    public static class GitGetter
    {
        public static void Get(string repoUrl, string branchName, string directoryToGetTo)
        {
            Console.WriteLine($"Starting to clone {repoUrl}");
            var cloneResult = Repository.Clone(repoUrl, directoryToGetTo, new CloneOptions{BranchName = branchName});
            Console.WriteLine($"Clone result: {cloneResult}");
        }
    }
}