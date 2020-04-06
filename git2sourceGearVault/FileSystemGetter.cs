using System.Collections.Generic;
using System.IO;

namespace git2sourceGearVault
{
    // For cases when CI/CD system like TeamCity checks code out from Git or other sources
    public static class FileSystemGetter
    {
        public static void Get(string source, string destination, params string[] ignoreDirs)
        {
            var ignores = new HashSet<string>(ignoreDirs);
            DirectoryCopy(source, destination, true, ignores);
        }

        // Copied from https://docs.microsoft.com/en-us/dotnet/standard/io/how-to-copy-directories and extended with ignores
        private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs, HashSet<string> ignores)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirName);
            }

            DirectoryInfo[] dirs = dir.GetDirectories();
            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }
        
            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string temppath = Path.Combine(destDirName, file.Name);
                file.CopyTo(temppath, false);
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    if (ignores.Contains(subdir.Name)) continue;
                    
                    string temppath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, temppath, copySubDirs, ignores);
                }
            }
        }
    }
}