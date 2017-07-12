//
// Arguments.cs
//
// Author:
// 	Jim Borden  <jim.borden@couchbase.com>
//
// Copyright (c) 2016 Couchbase, Inc All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using PowerArgs;

namespace PromoteBuild
{
    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling)]
    public class PromoteBuildProgram
    {
        private static readonly string _tempPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ".promotebuild");

        [HelpHook, ArgShortcut("-?"), ArgDescription("Shows this help")]
        public bool Help { get; set; }

        [ArgShortcut("-symbols"), ArgDescription("Create separate symbols package"), ArgDefaultValue(false)]
        public bool CreateSymbols { get; set; }

        [ArgActionMethod, ArgDescription("Overwrites the version of DLLs in the nupkg files in the specified directory with a new one")]
        [ArgExample("[mono] PromoteBuild.exe set -v 1.3.0 -d /path/to/files", "Changes the version of the nupkg files in the directory to 1.3.0")]
        public void Set(SetVersionArgs args)
        {
            if (Directory.Exists(_tempPath))
            {
                Directory.Delete(_tempPath, true);
            }

            Directory.CreateDirectory(_tempPath);
            try
            {
                foreach (var nupkg in Directory.EnumerateFiles(args.Directory, "*.nupkg"))
                {
                    var extractPath = Path.Combine(_tempPath, Path.GetFileNameWithoutExtension(nupkg));
                    Directory.CreateDirectory(extractPath);
                    if (CreateSymbols) {
                        ExtractZip(nupkg, extractPath, false);
                        ExtractZip(nupkg, extractPath + "-symbols", true);
                    } else {
                        ExtractZip(nupkg, extractPath, true);
                    }
                }

                foreach (var extractPath in Directory.EnumerateDirectories(_tempPath))
                {
                    ProcessNupkg(extractPath, args.Version, args.Directory);
                }
            }
            finally
            {
                Directory.Delete(_tempPath, true);
            }
        }

        private static void ProcessNupkg(string directory, string version, string outputDir)
        {
            var isSymbol = directory.EndsWith("-symbols");
            var nuspecPath = Directory.EnumerateFiles(directory, "*.nuspec").First();
            var oldVersion = CorrectNuspec(nuspecPath, version);
            var newFilename = isSymbol ? $"{directory.Split(Path.DirectorySeparatorChar).Last().Replace(oldVersion, version).Replace("-symbols","")}.symbols.nupkg" :
                $"{directory.Split(Path.DirectorySeparatorChar).Last().Replace(oldVersion, version)}.nupkg";
            CreateZip(directory, Path.Combine(outputDir, newFilename));
        }

        private static string CorrectNuspec(string sourceFile, string newVersion)
        {
            var text = File.ReadAllText(sourceFile);
            var oldVersion = Regex.Match(text, "<version>(.*?)</version>").Groups[1].Value;
            text = text.Replace(oldVersion, newVersion);
            File.WriteAllText(sourceFile, text);
            return oldVersion;
        }

        private static void ExtractZip(string sourceFile, string targetDirectory, bool includeSource)
        {
            using (var zipFile = new ZipFile(sourceFile))
            {
                foreach (var zipEntry in zipFile.OfType<ZipEntry>())
                {
                    var unzipPath = Path.Combine(targetDirectory, zipEntry.Name);
                    if (unzipPath.EndsWith("/")) {
                        continue; // Store directory entry (will be created later)
                    }

                    var directoryPath = Path.GetDirectoryName(unzipPath);

                    // create directory if needed
                    if (directoryPath.Length > 0)
                    {
                        Directory.CreateDirectory(directoryPath);
                    }

                    // unzip the file
                    var zipStream = zipFile.GetInputStream(zipEntry);
                    var buffer = new byte[4096];

                    var fileExtension = Path.GetExtension(unzipPath);
                    if (!includeSource && (fileExtension == ".pdb" || fileExtension == ".cs")) {
                        continue;
                    }

                    using (var unzippedFileStream = File.Create(unzipPath))
                    {
                        StreamUtils.Copy(zipStream, unzippedFileStream, buffer);
                    }
                }

                if (!includeSource) {
                    var srcDirectory = Path.Combine(targetDirectory, "src");
                    if (Directory.Exists(srcDirectory)) {
                        Directory.Delete(srcDirectory, true);
                    }
                }
            }
        }

        private static void CreateZip(string sourceDirectory, string targetFile)
        {
            FileStream fsOut = File.Create(targetFile);
            ZipOutputStream zipStream = new ZipOutputStream(fsOut);

            zipStream.SetLevel(3); //0-9, 9 being the highest level of compression

            // This setting will strip the leading part of the folder path in the entries, to
            // make the entries relative to the starting folder.
            // To include the full path for each entry up to the drive root, assign folderOffset = 0.
            int folderOffset = sourceDirectory.Length + (sourceDirectory.EndsWith("\\") ? 0 : 1);

            CompressFolder(sourceDirectory, zipStream, folderOffset);

            zipStream.IsStreamOwner = true; // Makes the Close also Close the underlying stream
            zipStream.Close();
        }

        private static void CompressFolder(string path, ZipOutputStream zipStream, int folderOffset)
        {

            string[] files = Directory.GetFiles(path);

            foreach (string filename in files)
            {

                FileInfo fi = new FileInfo(filename);

                string entryName = filename.Substring(folderOffset); // Makes the name in zip based on the folder
                entryName = ZipEntry.CleanName(entryName); // Removes drive from name and fixes slash direction
                ZipEntry newEntry = new ZipEntry(entryName);
                newEntry.DateTime = fi.LastWriteTime; // Note the zip format stores 2 second granularity
                newEntry.Size = fi.Length;

                zipStream.PutNextEntry(newEntry);
                byte[] buffer = new byte[4096];
                using (FileStream streamReader = File.OpenRead(filename))
                {
                    StreamUtils.Copy(streamReader, zipStream, buffer);
                }
                zipStream.CloseEntry();
            }
            string[] folders = Directory.GetDirectories(path);
            foreach (string folder in folders)
            {
                CompressFolder(folder, zipStream, folderOffset);
            }
        }
    }

    public class SetVersionArgs
    {
        [ArgRequired(PromptIfMissing = true), ArgShortcut("-v")]
        public string Version { get; set; }

        [ArgRequired(PromptIfMissing = true), ArgShortcut("-d")]
        public string Directory { get; set; }
    }
}

