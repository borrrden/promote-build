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
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using Mono.Cecil;
using PowerArgs;

namespace PromoteBuild
{
    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling)]
    public class PromoteBuildProgram
    {
        private static readonly string _tempPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ".promotebuild");

        [HelpHook, ArgShortcut("-?"), ArgDescription("Shows this help")]
        public bool Help { get; set; }

        [ArgActionMethod, ArgDescription("Overwrites the version of DLLs in the specified nupkg with a new one")]
        [ArgExample("[mono] PromoteBuild.exe set -v 1.3.0 -f Couchbase.Lite.1.3.0-build0100.nupkg", "Changes the version (1.3.0-build0100) of the nupkg to 1.3.0")]
        public void Set(SetVersionArgs args)
        {
            if (Directory.Exists(_tempPath))
            {
                Directory.Delete(_tempPath, true);
            }

            Directory.CreateDirectory(_tempPath);
            try
            {
                foreach (var nupkg in Directory.EnumerateFiles(args.Directory))
                {
                    var extractPath = Path.Combine(_tempPath, Path.GetFileNameWithoutExtension(nupkg));
                    Directory.CreateDirectory(extractPath);
                    ExtractZip(nupkg, extractPath);
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

        [ArgActionMethod, ArgDescription("Verifies the version of the DLLs inside the nupkg in the specified directory")]
        [ArgExample("[mono] PromoteBuild.exe verify -f Couchbase.Lite.1.3.0-build0100.nupkg", "Outputs the version of the dlls inside of Couchbase.Lite-build0100.nupkg")]
        [ArgExample("[mono] PromoteBuild.exe verify -v 1.3.0 -f Couchbase.Lite.1.3.0-build0100.nupkg", "Ensures that the version of the dlls in Couchbase.Lite-build0100.nupkg is 1.3.0")]
        public void Verify(VerifyVersionArgs args)
        {
            if (Directory.Exists(_tempPath))
            {
                Directory.Delete(_tempPath, true);
            }

            Directory.CreateDirectory(_tempPath);
            try
            {
                ExtractZip(args.Filename, _tempPath);
                var libsPath = Path.Combine(_tempPath, "lib");
                foreach (var d in Directory.EnumerateDirectories(libsPath))
                {
                    foreach (var f in Directory.EnumerateFiles(d, "Couchbase*.dll"))
                    {
                        var assembly = AssemblyDefinition.ReadAssembly(f);
                        var recordedVersion = assembly.CustomAttributes.First(x => x.AttributeType.Name == typeof(AssemblyInformationalVersionAttribute).Name).ConstructorArguments.First().Value;
                        Console.Write($"{f} -> {recordedVersion} ");
                        if (args.Version != null)
                        {
                            Console.Write(recordedVersion.Equals(args.Version) ? "matches " : "DOES NOT match ");
                            Console.Write($"{args.Version}");
                        }
                        Console.WriteLine();
                    }
                }
            }
            finally
            {
                Directory.Delete(_tempPath, true);
            }
        }

        private static void ProcessNupkg(string directory, string version, string outputDir)
        {
            var nuspecPath = Directory.EnumerateFiles(directory, "*.nuspec").First();
            CorrectNuspec(nuspecPath, version);
            var libsPath = Path.Combine(directory, "lib");
            var oldVersion = default(string);

            foreach (var d in Directory.EnumerateDirectories(libsPath))
            {
                var readerParams = new ReaderParameters
                {
                    AssemblyResolver = new RelativeAssemblyResolver(_tempPath, d.Split('/').Last())
                };

                foreach (var f in Directory.EnumerateFiles(d, "Couchbase*.dll"))
                {
                    var assembly = AssemblyDefinition.ReadAssembly(f, readerParams);
                    var recordedVersionAttribute = assembly.CustomAttributes.First(x => x.AttributeType.Name == typeof(AssemblyInformationalVersionAttribute).Name);
                    oldVersion = recordedVersionAttribute.ConstructorArguments.First().Value as string;
                    assembly.CustomAttributes.Remove(recordedVersionAttribute);
                    var newVersionAttribute = assembly.MainModule.Import(typeof(AssemblyInformationalVersionAttribute).GetConstructor(new[] { typeof(string) }));
                    var custom = new CustomAttribute(newVersionAttribute);
                    custom.ConstructorArguments.Add(new CustomAttributeArgument(assembly.MainModule.TypeSystem.String, version));
                    assembly.CustomAttributes.Add(custom);
                    assembly.Write(f);
                    RewriteWin32Res(f, oldVersion, version);
                    Console.WriteLine($"Processed {f}");
                }
            }

            var newFilename = $"{directory.Split('/').Last().Replace(oldVersion, version)}.nupkg";
            CreateZip(directory, Path.Combine(outputDir, newFilename));
        }

        private static void RewriteWin32Res(string filename, string oldVersion, string newVersion)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "edit-win32.sh",
                UseShellExecute = false,
                Arguments = $"{filename} {oldVersion} {newVersion}"
            };
            var proc = Process.Start(psi);
            proc.WaitForExit();
        }

        private static void CorrectNuspec(string sourceFile, string newVersion)
        {
            var text = File.ReadAllText(sourceFile);
            var oldVersion = Regex.Match(text, "<version>(.*?)</version>").Groups[1].Value;
            text = text.Replace(oldVersion, newVersion);
            File.WriteAllText(sourceFile, text);
        }

        private static void ExtractZip(string sourceFile, string targetDirectory)
        {
            using (var zipFile = new ZipFile(sourceFile))
            {
                foreach (var zipEntry in zipFile.OfType<ZipEntry>())
                {
                    var unzipPath = Path.Combine(targetDirectory, zipEntry.Name);
                    var directoryPath = Path.GetDirectoryName(unzipPath);

                    // create directory if needed
                    if (directoryPath.Length > 0)
                    {
                        Directory.CreateDirectory(directoryPath);
                    }

                    // unzip the file
                    var zipStream = zipFile.GetInputStream(zipEntry);
                    var buffer = new byte[4096];

                    using (var unzippedFileStream = File.Create(unzipPath))
                    {
                        StreamUtils.Copy(zipStream, unzippedFileStream, buffer);
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

    public class VerifyVersionArgs
    {
        [ArgShortcut("-v")]
        public string Version { get; set; }

        [ArgRequired(PromptIfMissing = true), ArgShortcut("-f")]
        public string Filename { get; set; }
    }
}

