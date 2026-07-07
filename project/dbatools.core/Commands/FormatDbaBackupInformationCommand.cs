#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Message;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Modifies backup history metadata to prepare database restores with different names, paths,
/// or locations. Port of public/Format-DbaBackupInformation.ps1; surface pinned by
/// migration/baselines/Format-DbaBackupInformation.json.
/// </summary>
[Cmdlet(VerbsCommon.Format, "DbaBackupInformation")]
[OutputType(typeof(Dataplat.Dbatools.Database.BackupHistory))]
public sealed class FormatDbaBackupInformationCommand : DbaBaseCmdlet
{
    /// <summary>Backup history objects from Select-DbaBackupInformation to transform for restore scenarios.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public object[] BackupHistory { get; set; } = null!;

    /// <summary>New database name (string) or old-to-new name map (hashtable).</summary>
    [Parameter(Position = 1)]
    public object? ReplaceDatabaseName { get; set; }

    /// <summary>Replaces occurrences of the original database name within physical file names.</summary>
    [Parameter]
    public SwitchParameter ReplaceDbNameInFile { get; set; }

    /// <summary>Destination directory for all data files during restore.</summary>
    [Parameter(Position = 2)]
    public string? DataFileDirectory { get; set; }

    /// <summary>Destination directory specifically for transaction log files.</summary>
    [Parameter(Position = 3)]
    public string? LogFileDirectory { get; set; }

    /// <summary>Destination directory for FileStream data files.</summary>
    [Parameter(Position = 4)]
    public string? DestinationFileStreamDirectory { get; set; }

    /// <summary>Prefix applied to all database names.</summary>
    [Parameter(Position = 5)]
    public string? DatabaseNamePrefix { get; set; }

    /// <summary>Prefix applied to the physical file names of all restored database files.</summary>
    [Parameter(Position = 6)]
    public string? DatabaseFilePrefix { get; set; }

    /// <summary>Suffix applied to the physical file names of all restored database files.</summary>
    [Parameter(Position = 7)]
    public string? DatabaseFileSuffix { get; set; }

    /// <summary>Changes the path where SQL Server will look for the backup files.</summary>
    [Parameter(Position = 8)]
    public string? RebaseBackupFolder { get; set; }

    /// <summary>Marks this as part of an ongoing restore sequence.</summary>
    [Parameter]
    public SwitchParameter Continue { get; set; }

    /// <summary>Maps specific logical file names to custom physical file paths.</summary>
    [Parameter(Position = 9)]
    public Hashtable? FileMapping { get; set; }

    /// <summary>Path separator character for file paths; defaults to backslash.</summary>
    [Parameter(Position = 10)]
    public string PathSep { get; set; } = "\\";

    private string? _replaceDatabaseNameType;

    protected override void BeginProcessing()
    {
        // PS [string] parameters read as '' when unbound; the body leans on '' -ne checks.
        DataFileDirectory = RestoreUtility.PsString(DataFileDirectory);
        LogFileDirectory = RestoreUtility.PsString(LogFileDirectory);
        DestinationFileStreamDirectory = RestoreUtility.PsString(DestinationFileStreamDirectory);
        DatabaseNamePrefix = RestoreUtility.PsString(DatabaseNamePrefix);
        DatabaseFilePrefix = RestoreUtility.PsString(DatabaseFilePrefix);
        DatabaseFileSuffix = RestoreUtility.PsString(DatabaseFileSuffix);
        RebaseBackupFolder = RestoreUtility.PsString(RebaseBackupFolder);
        PathSep = RestoreUtility.PsString(PathSep);

        WriteMessage(MessageLevel.Verbose, "Starting");
        if (ReplaceDatabaseName is not null)
        {
            object replaceValue = ReplaceDatabaseName is PSObject psValue ? psValue.BaseObject : ReplaceDatabaseName;
            // PS quirk preserved: anything whose ToString() is not the hashtable type name
            // counts as a 'single' rename value, not just strings.
            if (replaceValue is string || replaceValue.ToString() != "System.Collections.Hashtable")
            {
                WriteMessage(MessageLevel.Verbose, "String passed in for DB rename");
                _replaceDatabaseNameType = "single";
            }
            else if (replaceValue is Hashtable || replaceValue.ToString() == "System.Collections.Hashtable")
            {
                WriteMessage(MessageLevel.Verbose, "Hashtable passed in for DB rename");
                _replaceDatabaseNameType = "multi";
            }
            else
            {
                WriteMessage(MessageLevel.Verbose, $"ReplacemenDatabaseName is {replaceValue.GetType()} - {RestoreUtility.PsStringify(ReplaceDatabaseName)}");
            }
        }
        if (TestBound("DataFileDirectory") && DataFileDirectory!.EndsWith(PathSep, StringComparison.Ordinal))
        {
            DataFileDirectory = Regex.Replace(DataFileDirectory, ".$", "");
        }
        if (TestBound("DestinationFileStreamDirectory") && DestinationFileStreamDirectory!.EndsWith(PathSep, StringComparison.Ordinal))
        {
            DestinationFileStreamDirectory = Regex.Replace(DestinationFileStreamDirectory, ".$", "");
        }
        if (TestBound("LogFileDirectory") && LogFileDirectory!.EndsWith(PathSep, StringComparison.Ordinal))
        {
            LogFileDirectory = Regex.Replace(LogFileDirectory, ".$", "");
        }
        if (TestBound("RebaseBackupFolder") && RebaseBackupFolder!.EndsWith(PathSep, StringComparison.Ordinal))
        {
            RebaseBackupFolder = Regex.Replace(RebaseBackupFolder, ".$", "");
        }
    }

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        foreach (object history in BackupHistory)
        {
            if (!PsProperty.Has(history, "OriginalDatabase"))
            {
                PsProperty.AddNote(history, "OriginalDatabase", PsProperty.Get(history, "Database"));
            }
            if (!PsProperty.Has(history, "OriginalFileList"))
            {
                // PS adds the note with '' and immediately points it at the live FileList
                // collection — the same references the loop below mutates. Preserved.
                PsProperty.AddNote(history, "OriginalFileList", "");
                PsProperty.Set(history, "OriginalFileList", PsProperty.Get(history, "FileList"));
            }
            if (!PsProperty.Has(history, "OriginalFullName"))
            {
                PsProperty.AddNote(history, "OriginalFullName", PsProperty.Get(history, "FullName"));
            }
            if (!PsProperty.Has(history, "IsVerified"))
            {
                PsProperty.AddNote(history, "IsVerified", false);
            }
            string currentType = RestoreUtility.PsStringify(PsProperty.Get(history, "Type"));
            if (PsString.Eq(currentType, "Full"))
            {
                PsProperty.Set(history, "Type", "Database");
            }
            else if (PsString.Eq(currentType, "Differential"))
            {
                PsProperty.Set(history, "Type", "Database Differential");
            }
            else if (PsString.Eq(currentType, "Log"))
            {
                PsProperty.Set(history, "Type", "Transaction Log");
            }

            if (_replaceDatabaseNameType == "single" && RestoreUtility.PsStringify(ReplaceDatabaseName) != "")
            {
                PsProperty.Set(history, "Database", ReplaceDatabaseName is PSObject ps ? ps.BaseObject : ReplaceDatabaseName);
                WriteMessage(MessageLevel.Verbose, $"New DbName (String) = {RestoreUtility.PsStringify(PsProperty.Get(history, "Database"))}");
            }
            else if (_replaceDatabaseNameType == "multi")
            {
                Hashtable? map = (ReplaceDatabaseName is PSObject psMap ? psMap.BaseObject : ReplaceDatabaseName) as Hashtable;
                object? mapped = map?[RestoreUtility.PsStringify(PsProperty.Get(history, "Database"))];
                if (mapped is not null)
                {
                    PsProperty.Set(history, "Database", mapped);
                    WriteMessage(MessageLevel.Verbose, $"New DbName (Hash) = {RestoreUtility.PsStringify(PsProperty.Get(history, "Database"))}");
                }
            }
            PsProperty.Set(history, "Database", DatabaseNamePrefix + RestoreUtility.PsStringify(PsProperty.Get(history, "Database")));

            // Capture rename values before entering the ForEach-Object pipeline to ensure correct
            // scoping and to treat both database names literally during file renames
            string originalDb = RestoreUtility.PsStringify(PsProperty.Get(history, "OriginalDatabase"));
            string newDb = RestoreUtility.PsStringify(PsProperty.Get(history, "Database"));

            if (PsProperty.Get(history, "FileList") is IEnumerable fileList and not string)
            {
                foreach (object? fileRaw in fileList)
                {
                    if (fileRaw is null)
                        continue;
                    object file = fileRaw;
                    if (FileMapping is not null)
                    {
                        object? mappedPath = FileMapping[RestoreUtility.PsStringify(PsProperty.Get(file, "LogicalName"))];
                        if (mappedPath is not null)
                        {
                            PsProperty.Set(file, "PhysicalName", mappedPath);
                        }
                    }
                    else
                    {
                        WriteMessage(MessageLevel.Verbose, $" 1 PhysicalName = {RestoreUtility.PsStringify(PsProperty.Get(file, "PhysicalName"))} ");

                        // Instead of using [System.IO.FileInfo] which has cross-platform issues,
                        // manually parse the path using both separators to handle Windows paths on Linux and vice versa
                        string originalPath = RestoreUtility.PsStringify(PsProperty.Get(file, "PhysicalName"));

                        // Get just the filename by splitting on both separators
                        string[] pathParts = Regex.Split(originalPath, "[/\\\\]");
                        string fileName = pathParts[pathParts.Length - 1];
                        string baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);
                        string extension = System.IO.Path.GetExtension(fileName);

                        // Handle MacOS returning full path for BaseName
                        if (PathSep.Length > 0)
                        {
                            string[] baseParts = baseName.Split(PathSep.ToCharArray());
                            baseName = baseParts[baseParts.Length - 1];
                        }

                        if (ReplaceDbNameInFile.ToBool())
                        {
                            baseName = Regex.Replace(
                                baseName,
                                Regex.Escape(originalDb),
                                new MatchEvaluator(_ => newDb),
                                RegexOptions.IgnoreCase);
                        }

                        // Determine restore directory based on file type
                        string? restoreDir = null;
                        string fileType = RestoreUtility.PsStringify(PsProperty.Get(file, "Type"));
                        string fileTypeAlt = RestoreUtility.PsStringify(PsProperty.Get(file, "FileType"));
                        if (PsString.Eq(fileType, "D") || PsString.Eq(fileTypeAlt, "D"))
                        {
                            if (DataFileDirectory != "")
                            {
                                restoreDir = DataFileDirectory;
                            }
                        }
                        else if (PsString.Eq(fileType, "L") || PsString.Eq(fileTypeAlt, "L"))
                        {
                            if (LogFileDirectory != "")
                            {
                                restoreDir = LogFileDirectory;
                            }
                            else if (DataFileDirectory != "")
                            {
                                restoreDir = DataFileDirectory;
                            }
                        }
                        else if (PsString.Eq(fileType, "S") || PsString.Eq(fileTypeAlt, "S"))
                        {
                            if (DestinationFileStreamDirectory != "")
                            {
                                restoreDir = DestinationFileStreamDirectory;
                            }
                            else if (DataFileDirectory != "")
                            {
                                restoreDir = DataFileDirectory;
                            }
                        }

                        // Fallback to extracting directory from original path if no destination specified
                        restoreDir ??= Regex.Replace(originalPath, "[/\\\\][^/\\\\]+$", "");

                        PsProperty.Set(file, "PhysicalName", restoreDir + PathSep + DatabaseFilePrefix + baseName + DatabaseFileSuffix + extension);
                        WriteMessage(MessageLevel.Verbose, $"PhysicalName = {RestoreUtility.PsStringify(PsProperty.Get(file, "PhysicalName"))} ");
                    }
                }
            }

            object? fullNameRaw = PsProperty.Get(history, "FullName");
            string firstFullName = fullNameRaw is IList { Count: > 0 } fullNameList
                ? RestoreUtility.PsStringify(fullNameList[0])
                : fullNameRaw is string s && s.Length > 0 ? s[0].ToString() : "";
            if (RebaseBackupFolder != "" && !Regex.IsMatch(firstFullName, "http", RegexOptions.IgnoreCase))
            {
                WriteMessage(MessageLevel.Verbose, "Rebasing backup files");

                if (fullNameRaw is IList rebaseList)
                {
                    for (int j = 0; j < rebaseList.Count; j++)
                    {
                        System.IO.FileInfo fileInfo = new(RestoreUtility.PsStringify(rebaseList[j]));
                        rebaseList[j] = RebaseBackupFolder + PathSep + System.IO.Path.GetFileNameWithoutExtension(fileInfo.Name) + fileInfo.Extension;
                    }
                }
            }

            WriteObject(history);
        }
    }
}
