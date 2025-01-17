﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Calamari.Integration.FileSystem
{
    public abstract class CalamariPhysicalFileSystem : ICalamariFileSystem
    {
        public static CalamariPhysicalFileSystem GetPhysicalFileSystem()
        {
            if (CalamariEnvironment.IsRunningOnNix)
            {
                return new NixCalamariPhysicalFileSystem();
            }

            return new WindowsPhysicalFileSystem();
        }

        /// <summary>
        /// For file operations, try again after 100ms and again every 200ms after that
        /// </summary>
        static readonly RetryInterval RetryIntervalForFileOperations = new RetryInterval(100, 200, 2);

        /// <summary>
        /// For file operations, retry constantly up to one minute
        /// </summary>
        /// <remarks>
        /// Windows services can hang on to files for ~30s after the service has stopped as background
        /// threads shutdown or are killed for not shutting down in a timely fashion
        /// </remarks>
        static RetryTracker GetRetryTracker()
        {
            return new RetryTracker(maxRetries:10000, 
                timeLimit: TimeSpan.FromMinutes(1), 
                retryInterval: RetryIntervalForFileOperations);
        }

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
        }

        public bool DirectoryIsEmpty(string path)
        {
            try
            {
                return !Directory.GetFileSystemEntries(path).Any();
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void DeleteFile(string path)
        {
            DeleteFile(path, FailureOptions.ThrowOnFailure);
        }

        public void DeleteFile(string path, FailureOptions options)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var retry = GetRetryTracker();

            while (retry.Try())
            {
                try
                {
                    if (File.Exists(path))
                    {
                        if (retry.IsNotFirstAttempt)
                        {
                            File.SetAttributes(path, FileAttributes.Normal);
                        }
                        File.Delete(path);
                    }
                    break;
                }
                catch (Exception ex)
                {
                    if (retry.CanRetry())
                    {
                        if (retry.ShouldLogWarning())
                        {
                            Log.VerboseFormat("Retry #{0} on delete file '{1}'. Exception: {2}", retry.CurrentTry, path, ex.Message);
                        }
                        Thread.Sleep(retry.Sleep());
                    }
                    else
                    {
                        if (options == FailureOptions.ThrowOnFailure)
                        {
                            throw;
                        }
                    }
                }
            }
        }

        public void DeleteDirectory(string path)
        {
            Directory.Delete(path, true);
        }

        public void DeleteDirectory(string path, FailureOptions options)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var retry = GetRetryTracker();
            while (retry.Try())
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        var dir = new DirectoryInfo(path);
                        dir.Attributes = dir.Attributes & ~FileAttributes.ReadOnly;
                        dir.Delete(true);
                        EnsureDirectoryDeleted(path, options);
                    }
                    break;
                }
                catch (Exception ex)
                {
                    if (retry.CanRetry())
                    {
                        if (retry.ShouldLogWarning())
                        {
                            Log.VerboseFormat("Retry #{0} on delete directory '{1}'. Exception: {2}", retry.CurrentTry, path, ex.Message);
                        }
                        Thread.Sleep(retry.Sleep());
                    }
                    else
                    {
                        if (options == FailureOptions.ThrowOnFailure)
                        {
                            throw;
                        }
                    }
                }
            }
        }

        static void EnsureDirectoryDeleted(string path, FailureOptions failureOptions)
        {
            var retry = GetRetryTracker(); 

            while (retry.Try())
            {
                if (!Directory.Exists(path))
                    return;

                if (retry.CanRetry() && retry.ShouldLogWarning())
                    Log.VerboseFormat("Waiting for directory '{0}' to be deleted", path);
            }

            var message = $"Directory '{path}' still exists, despite requested deletion";

            if (failureOptions == FailureOptions.ThrowOnFailure)
                throw new Exception(message);

            Log.Verbose(message);
        }

        public virtual IEnumerable<string> EnumerateFiles(string parentDirectoryPath, params string[] searchPatterns)
        {
            var parentDirectoryInfo = new DirectoryInfo(parentDirectoryPath);

            return searchPatterns.Length == 0
                ? parentDirectoryInfo.GetFiles("*", SearchOption.TopDirectoryOnly).Select(fi => fi.FullName)
                : searchPatterns.SelectMany(pattern => parentDirectoryInfo.GetFiles(pattern, SearchOption.TopDirectoryOnly).Select(fi => fi.FullName));
        }

        public virtual IEnumerable<string> EnumerateFilesRecursively(string parentDirectoryPath, params string[] searchPatterns)
        {
            var parentDirectoryInfo = new DirectoryInfo(parentDirectoryPath);

            return searchPatterns.Length == 0
                ? parentDirectoryInfo.GetFiles("*", SearchOption.AllDirectories).Select(fi => fi.FullName)
                : searchPatterns.SelectMany(pattern => parentDirectoryInfo.GetFiles(pattern, SearchOption.AllDirectories).Select(fi => fi.FullName));
        }

        public IEnumerable<string> EnumerateDirectories(string parentDirectoryPath)
        {
            return Directory.EnumerateDirectories(parentDirectoryPath);
        }

        public IEnumerable<string> EnumerateDirectoriesRecursively(string parentDirectoryPath)
        {
            return Directory.EnumerateDirectories(parentDirectoryPath, "*", SearchOption.AllDirectories);
        }

        public long GetFileSize(string path)
        {
            return new FileInfo(path).Length;
        }

        public string ReadFile(string path)
        {
            return File.ReadAllText(path);
        }

        public byte[] ReadAllBytes(string path)
        {
            return File.ReadAllBytes(path);
        }

        public void AppendToFile(string path, string contents)
        {
            File.AppendAllText(path, contents);
        }

        public void OverwriteFile(string path, string contents)
        {
            File.WriteAllText(path, contents);
        }

        public void OverwriteFile(string path, string contents, Encoding encoding)
        {
            File.WriteAllText(path, contents, encoding);
        }

        public Stream OpenFile(string path, FileAccess access, FileShare share)
        {
            return OpenFile(path, FileMode.OpenOrCreate, access, share);
        }

        public Stream OpenFile(string path, FileMode mode, FileAccess access, FileShare share)
        {
            return new FileStream(path, mode, access, share);
        }

        public Stream CreateTemporaryFile(string extension, out string path)
        {
            if (!extension.StartsWith("."))
                extension = "." + extension;

            path = Path.Combine(GetTempBasePath(), Guid.NewGuid() + extension);

            return OpenFile(path, FileAccess.ReadWrite, FileShare.Read);
        }

        static string GetTempBasePath()
        {
            var path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            path = Path.Combine(path, Assembly.GetEntryAssembly().GetName().Name);
            path = Path.Combine(path, "Temp");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }

        public string CreateTemporaryDirectory()
        {
            var path = Path.Combine(GetTempBasePath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(path);
            return path;
        }

        public void PurgeDirectory(string targetDirectory, FailureOptions options)
        {
            PurgeDirectory(targetDirectory, fi => true, options);
        }

        public void PurgeDirectory(string targetDirectory, FailureOptions options, CancellationToken cancel)
        {
            PurgeDirectory(targetDirectory, fi => true, options, cancel);
        }

        public void PurgeDirectory(string targetDirectory, Predicate<IFileInfo> include, FailureOptions options)
        {
            PurgeDirectory(targetDirectory, include, options, CancellationToken.None);
        }

        void PurgeDirectory(string targetDirectory, Predicate<IFileInfo> include, FailureOptions options, CancellationToken cancel, bool includeTarget = false)
        {
            if (!DirectoryExists(targetDirectory))
            {
                return;
            }

            foreach (var file in EnumerateFiles(targetDirectory))
            {
                cancel.ThrowIfCancellationRequested();

                if (include != null)
                {
                    var info = new FileInfoAdapter(new FileInfo(file));
                    if (!include(info))
                    {
                        continue;
                    }
                }

                DeleteFile(file, options);
            }

            foreach (var directory in EnumerateDirectories(targetDirectory))
            {
                cancel.ThrowIfCancellationRequested();

                var info = new DirectoryInfo(directory);
                if ((info.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
                    Directory.Delete(directory);
                }
                else
                {
                    PurgeDirectory(directory, include, options, cancel, includeTarget: true);
                }
            }

            if (includeTarget && DirectoryIsEmpty(targetDirectory))
                DeleteDirectory(targetDirectory, options);
        }

        public void OverwriteAndDelete(string originalFile, string temporaryReplacement)
        {
            var backup = originalFile + ".backup" + Guid.NewGuid();

            if (!File.Exists(originalFile))
                File.Copy(temporaryReplacement, originalFile, true);
            else
                File.Replace(temporaryReplacement, originalFile, backup);

            File.Delete(temporaryReplacement);
            if (File.Exists(backup))
                File.Delete(backup);
        }

        public void WriteAllBytes(string filePath, byte[] data)
        {
            File.WriteAllBytes(filePath, data);
        }

        public string RemoveInvalidFileNameChars(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var invalidPathChars = Path.GetInvalidPathChars();
            var invalidFileChars = Path.GetInvalidFileNameChars();

            var result = new StringBuilder(path.Length);
            for (var i = 0; i < path.Length; i++)
            {
                var c = path[i];
                if (!invalidPathChars.Contains(c) && !invalidFileChars.Contains(c))
                {
                    result.Append(c);
                }
            }
            return result.ToString();
        }

        public void MoveFile(string sourceFile, string destinationFile)
        {
            File.Move(sourceFile, destinationFile);
        }

        public Encoding GetFileEncoding(string path)
        {
            using (var reader = new StreamReader(path, Encoding.Default, true))
            {
                reader.Peek();
                return reader.CurrentEncoding;
            }
        }

        public void EnsureDirectoryExists(string directoryPath)
        {
            if (!DirectoryExists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }

        // ReSharper disable AssignNullToNotNullAttribute

        public int CopyDirectory(string sourceDirectory, string targetDirectory)
        {
            return CopyDirectory(sourceDirectory, targetDirectory, CancellationToken.None);
        }

        public int CopyDirectory(string sourceDirectory, string targetDirectory, CancellationToken cancel)
        {
            if (!DirectoryExists(sourceDirectory))
                return 0;

            if (!DirectoryExists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            int count = 0;
            var files = Directory.GetFiles(sourceDirectory, "*");
            foreach (var sourceFile in files)
            {
                cancel.ThrowIfCancellationRequested();

                var targetFile = Path.Combine(targetDirectory, Path.GetFileName(sourceFile));
                CopyFile(sourceFile, targetFile);
                count++;
            }

            foreach (var childSourceDirectory in Directory.GetDirectories(sourceDirectory))
            {
                var name = Path.GetFileName(childSourceDirectory);
                var childTargetDirectory = Path.Combine(targetDirectory, name);
                count += CopyDirectory(childSourceDirectory, childTargetDirectory, cancel);
            }

            return count;
        }

        public void CopyFile(string sourceFile, string targetFile)
        {
            var retry = GetRetryTracker();
            while (retry.Try())
            {
                try
                {
                    File.Copy(sourceFile, targetFile, true);
                    return;
                }
                catch (Exception ex)
                {
                    if (retry.CanRetry())
                    {
                        if (retry.ShouldLogWarning())
                        {
                            Log.VerboseFormat("Retry #{0} on copy '{1}'. Exception: {2}", retry.CurrentTry,  targetFile, ex.Message);
                        }
                        Thread.Sleep(retry.Sleep());
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }

        public void EnsureDiskHasEnoughFreeSpace(string directoryPath)
        {
            EnsureDiskHasEnoughFreeSpace(directoryPath, 500 * 1024 * 1024);
        }

        public void EnsureDiskHasEnoughFreeSpace(string directoryPath, long requiredSpaceInBytes)
        {
            ulong totalNumberOfFreeBytes;

            var success = GetFiskFreeSpace(directoryPath, out totalNumberOfFreeBytes);
            if (!success)
                return;

            // Always make sure at least 500MB are available regardless of what we need 
            var required = requiredSpaceInBytes < 0 ? 0 : (ulong)requiredSpaceInBytes;
            required = Math.Max(required, 500L * 1024 * 1024);
            if (totalNumberOfFreeBytes < required)
            {
                throw new IOException(string.Format("The drive containing the directory '{0}' on machine '{1}' does not have enough free disk space available for this operation to proceed. The disk only has {2} available; please free up at least {3}.", directoryPath, Environment.MachineName, totalNumberOfFreeBytes.ToFileSizeString(), required.ToFileSizeString()));
            }
        }

        public string GetFullPath(string relativeOrAbsoluteFilePath)
        {
            if (!Path.IsPathRooted(relativeOrAbsoluteFilePath))
            {
                relativeOrAbsoluteFilePath = Path.Combine(Environment.CurrentDirectory, relativeOrAbsoluteFilePath);
            }

            relativeOrAbsoluteFilePath = Path.GetFullPath(relativeOrAbsoluteFilePath);
            return relativeOrAbsoluteFilePath;
        }

        protected abstract bool GetFiskFreeSpace(string directoryPath, out ulong totalNumberOfFreeBytes);

    }
}
