﻿// Kerbal Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using KSPDev.ConfigUtils;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System;
using UnityEngine;

namespace KSPDev.LogConsole {

/// <summary>A log capturer that writes logs on disk.</summary>
/// <remarks>
/// <para>Three files are created: <list type="bullet">
/// <item><c>INFO</c> that includes all logs;</item>
/// <item><c>WARNING</c> which captures warnings and errors;</item>
/// <item><c>ERROR</c> for the errors (including exceptions).</item>
/// </list></para>
/// <para>Persistent logging must be explicitly enabled via <c>PersistentLogs-settings.cfg</c>
/// </para>
/// </remarks>
[PersistentFieldsFileAttribute("KSPDev/LogConsole/Plugins/PluginData/settings.cfg",
                               "PersistentLog")]
sealed class PersistentLogAggregator : BaseLogAggregator {
  [PersistentField("enableLogger")]
  bool enableLogger = true;
  
  [PersistentField("logFilesPath")]
  string logFilePath = "GameData/KSPDev/logs";
  
  /// <summary>Prefix for every log file name.</summary>
  [PersistentField("logFilePrefix")]
  string logFilePrefix = "KSPDev-LOG";
  
  /// <summary>Format of the timestamp in the file.</summary>
  [PersistentField("logTsFormat")]
  string logTsFormat = "yyMMdd\\THHmmss";

  /// <summary>Specifies if INFO file should be written.</summary>
  [PersistentField("writeInfoFile")]
  bool writeInfoFile = true;

  /// <summary>Specifies if WARNING file should be written.</summary>
  [PersistentField("writeWarningFile")]
  bool writeWarningFile = true;

  /// <summary>Specifies if ERROR file should be written.</summary>
  [PersistentField("writeErrorFile")]
  bool writeErrorFile = true;

  /// <summary>Limits total number of log files in the directory.</summary>
  /// <remarks>
  /// Only files that match file prefix are counted. Older files will be drop to stisfy the limit.
  /// </remarks>
  [PersistentField("cleanupPolicy/totalFiles")]
  int totalFiles = -1;

  /// <summary>Limits total size of all log files in the directory.</summary>
  /// <remarks>
  /// Only files that match file prefix are counted. Older files will be drop to stisfy the limit.
  /// </remarks>
  [PersistentField("cleanupPolicy/totalSizeMb")]
  int totalSizeMb = -1;

  /// <summary>Maximum age of the log files in the directory.</summary>
  /// <remarks>
  /// Only files that match file prefix are counted. Older files will be drop to stisfy the limit.
  /// </remarks>
  [PersistentField("cleanupPolicy/maxAgeHours")]
  float maxAgeHours = -1;

  /// <summary>Specifies if new record should be aggregated and persisted.</summary>
  bool writeLogsToDisk = false;

  /// <summary>A writer that gets all the logs.</summary>
  StreamWriter infoLogWriter;
  
  /// <summary>A writer for <c>WARNING</c>, <c>ERROR</c> and <c>EXCEPTION</c> logs.</summary>
  StreamWriter warningLogWriter;

  /// <summary>Writer for <c>ERROR</c> and <c>EXCEPTION</c> logs.</summary>
  StreamWriter errorLogWriter;

  /// <inheritdoc/>
  public override IEnumerable<LogRecord> GetLogRecords() {
    return logRecords;  // It's always empty.
  }

  /// <inheritdoc/>
  public override void ClearAllLogs() {
    // Cannot clear persistent log so, restart the files instead.
    StartLogFiles();
  }

  /// <inheritdoc/>
  protected override void DropAggregatedLogRecord(LinkedListNode<LogRecord> node) {
    // Do nothing since there is no memory state in the aggregator.
  }

  /// <inheritdoc/>
  protected override void AggregateLogRecord(LogRecord logRecord) {
    if (!writeLogsToDisk) {
      return;
    }
    var message = logRecord.MakeTitle();
    var type = logRecord.srcLog.type;
    if (type == LogType.Exception && logRecord.srcLog.stackTrace.Length > 0) {
      message += "\n" + logRecord.srcLog.stackTrace;
    }
    try {
      if (infoLogWriter != null) {
        infoLogWriter.WriteLine(message);
      }
      if (warningLogWriter != null
          && (type == LogType.Warning || type == LogType.Error || type == LogType.Exception)) {
        warningLogWriter.WriteLine(message);
      }
      if (errorLogWriter != null && (type == LogType.Error || type == LogType.Exception)) {
        errorLogWriter.WriteLine(message);
      }
    } catch (Exception ex) {
      writeLogsToDisk = false;
      Debug.LogException(ex);
      Debug.LogError("Persistent log agregator failed to write a record. Logging disabled");
    }
  }

  /// <inheritdoc/>
  public override void StartCapture() {
    base.StartCapture();
    StartLogFiles();
    PersistentLogAggregatorFlusher.activeAggregators.Add(this);
    if (writeLogsToDisk) {
      Debug.Log("Persistent aggregator started");
    } else {
      Debug.LogWarning("Persistent aggregator disabled");
    }
  }

  /// <inheritdoc/>
  public override void StopCapture() {
    Debug.Log("Stopping a persistent aggregator...");
    base.StopCapture();
    StopLogFiles();
    PersistentLogAggregatorFlusher.activeAggregators.Remove(this);
  }

  /// <inheritdoc/>
  public override bool FlushBufferedLogs() {
    // Flushes accumulated logs to disk. In case of disk error the logging is disabled.
    var res = base.FlushBufferedLogs();
    if (res && writeLogsToDisk) {
      try {
        if (infoLogWriter != null) {
          infoLogWriter.Flush();
        }
        if (warningLogWriter != null) {
          warningLogWriter.Flush();
        }
        if (errorLogWriter != null) {
          errorLogWriter.Flush();
        }
      } catch (Exception ex) {
        writeLogsToDisk = false;  // Must be the first statement in the catch section!
        Debug.LogException(ex);
        Debug.LogError("Something went wrong when flushing data to disk. Disabling logging.");
      }
    }
    return res;
  }

  /// <inheritdoc/>
  protected override bool CheckIsFiltered(LogInterceptor.Log log) {
    return false;  // Persist any log!
  }

  /// <summary>Creates new logs files and redirects logs to there.</summary>
  void StartLogFiles() {
    StopLogFiles();  // In case something was opened.
    try {
      CleanupLogFiles();
    } catch (Exception ex) {
      Debug.LogException(ex);
      Debug.LogError("Error happen while cleaning up old logs");
    }
    try {
      if (enableLogger) {
        if (logFilePath.Length > 0) {
          Directory.CreateDirectory(logFilePath);
        }
        var tsSuffix = DateTime.Now.ToString(logTsFormat);
        if (writeInfoFile) {
          infoLogWriter = new StreamWriter(Path.Combine(
              logFilePath, String.Format("{0}.{1}.INFO.txt", logFilePrefix, tsSuffix)));
        }
        if (writeWarningFile) {
          warningLogWriter = new StreamWriter(Path.Combine(
              logFilePath, String.Format("{0}.{1}.WARNING.txt", logFilePrefix, tsSuffix)));
        }
        if (writeErrorFile) {
          errorLogWriter = new StreamWriter(Path.Combine(
              logFilePath, String.Format("{0}.{1}.ERROR.txt", logFilePrefix, tsSuffix)));
        }
      }
      writeLogsToDisk = infoLogWriter != null || warningLogWriter != null || errorLogWriter != null;
    } catch (Exception ex) {
      writeLogsToDisk = false;  // Must be the first statement in the catch section!
      Debug.LogException(ex);
      Debug.LogError("Not enabling logging to disk due to errors");
    }
  }

  /// <summary>Flushes and closes all opened log files.</summary>
  void StopLogFiles() {
    try {
      if (infoLogWriter != null) {
        infoLogWriter.Close();
      }
      if (warningLogWriter != null) {
        warningLogWriter.Close();
      }
      if (errorLogWriter != null) {
        errorLogWriter.Close();
      }
    } catch (Exception ex) {
      Debug.LogException(ex);
    }
    infoLogWriter = null;
    warningLogWriter = null;
    errorLogWriter = null;
    writeLogsToDisk = false;
  }

  /// <summary>Drops extra log files.</summary>
  void CleanupLogFiles() {
    if (totalFiles < 0 && totalSizeMb < 0 && maxAgeHours < 0) {
      return;
    }
    var logFiles = Directory.GetFiles(logFilePath, logFilePrefix + ".*")
        .Select(x => new FileInfo(x))
        .OrderBy(x => x.CreationTimeUtc)
        .ToArray();
    if (logFiles.Length == 0) {
      Debug.Log("No log files found. Nothing to do");
      return;
    }
    var limitTotalSize = logFiles.Sum(x => x.Length);
    var limitExtraFiles = logFiles.Count() - totalFiles;
    var limitAge = DateTime.UtcNow.AddHours(-maxAgeHours);
    Debug.LogFormat("Found peristent logs: totalFiles={0}, totalSize={1}, oldestDate={2}",
                    logFiles.Count(), limitTotalSize, logFiles.Min(x => x.CreationTimeUtc));
    for (var i = 0; i < logFiles.Count(); i++) {
      var fileinfo = logFiles[i];
      if (totalFiles > 0 && limitExtraFiles > 0) {
        Debug.LogFormat("Drop log file due to too many log files exist: {0}", fileinfo.FullName);
        File.Delete(fileinfo.FullName);
      } else if (totalSizeMb > 0 && limitTotalSize > totalSizeMb * 1024 * 1024) {
        Debug.LogFormat("Drop log file due to too large total size: {0}", fileinfo.FullName);
        File.Delete(fileinfo.FullName);
      } else if (maxAgeHours > 0 && fileinfo.CreationTimeUtc < limitAge) {
        Debug.LogFormat("Drop log file due to it's too old: {0}", fileinfo.FullName);
        File.Delete(fileinfo.FullName);
      }
      --limitExtraFiles;
      limitTotalSize -= fileinfo.Length;
    }
  }
}

/// <summary>A helper class to periodically flush logs to disk.</summary>
/// <remarks>Also, does flush on scene change or game exit.</remarks>
[KSPAddon(KSPAddon.Startup.EveryScene, false /*once*/)]
sealed class PersistentLogAggregatorFlusher : MonoBehaviour {
  /// <summary>A list of persistent aggergators that need state flushing.</summary>
  public static HashSet<PersistentLogAggregator> activeAggregators =
      new HashSet<PersistentLogAggregator>();

  /// <summary>A delay between flushes.</summary>
  public static float persistentLogsFlushPeriod = 0.2f;  // Seconds.

  void Awake() {
    StartCoroutine(FlushLogsCoroutine());
  }

  void OnDestroy() {
    FlushAllAggregators();
  }

  /// <summary>Flushes all registered persistent aggregators.</summary>
  static void FlushAllAggregators() {
    var aggregators = activeAggregators.ToArray();
    foreach (var aggregator in aggregators) {
      aggregator.FlushBufferedLogs();
    }
  }

  /// <summary>Flushes logs to disk periodically.</summary>
  /// <remarks>This method never returns.</remarks>
  /// <returns>Delay till next flush.</returns>
  IEnumerator FlushLogsCoroutine() {
    //FIXME InvokeRepeating?
    while (true) {
      yield return new WaitForSeconds(persistentLogsFlushPeriod);
      FlushAllAggregators();
    }
  }
}

} // namespace KSPDev
