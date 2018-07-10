﻿// Kerbal Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using UnityEngine;
using System;
using System.Text;
using System.Collections.Generic;
using System.IO;

namespace KSPDev.LogConsole {

/// <summary>A wrapper class to hold log record(s).</summary>
public class LogRecord {
  // Log text generation constants.
  const string InfoPrefix = "INFO";
  const string WarningPrefix = "WARNING";
  const string ErrorPrefix = "ERROR";
  const string ExceptionPrefix = "EXCEPTION";
  const string RepeatedPrefix = "REPEATED:";
  
  /// <summary>Format of a timestamp in the logs.</summary>
  const string TimestampFormat = "yyMMdd\\THHmmss.fff";

  /// <summary>A maximum size of title of a regular log record.</summary>
  /// <remarks>
  /// Used to reserve memory when building log text. Too big value will waste memory and too small
  /// value may impact performance. Keep it reasonable.
  /// </remarks>
  const int TitleMaxSize = 200;

  /// <summary>An original Unity log record.</summary>  
  public readonly LogInterceptor.Log srcLog;

  /// <summary>A unique ID of the log.</summary>
  /// <remarks>Don't use it for ordering since it's not defined how this ID is generated.</remarks>
  public int lastId {
    get { return _lastId; }
  }
  int _lastId;

  /// <summary>Timestamp of the log in local world (non-game) time.</summary>
  public DateTime timestamp {
    get { return _timestamp; }
  }
  DateTime _timestamp;

  /// <summary>Number of logs merged into this record so far.</summary>   
  int mergedLogs = 1;
  
  /// <summary>A lazzy cache for the log hash code.</summary>
  int? similarityHash;
  
  /// <summary>A generic wrapper for Unity log records.</summary>
  /// <param name="log">A Unity log record.</param>
  public LogRecord(LogInterceptor.Log log) {
    srcLog = log;
    _lastId = log.id;
    _timestamp = log.timestamp;
  }

  /// <summary>Makes a copy of the existing LogRecord.</summary>
  public LogRecord(LogRecord logRecord) {
    srcLog = logRecord.srcLog;
    _lastId = logRecord._lastId;
    _timestamp = logRecord.timestamp;
    mergedLogs = logRecord.mergedLogs;
    similarityHash = logRecord.similarityHash;
  }

  /// <summary>Returns a hash code that is indentical for the *similar* log records.</summary>
  /// <remarks>
  /// This method is supposed to be called very frequiently so, caching the code is a good idea.
  /// </remarks>
  /// <returns>A hash code of the *similar* fields.</returns>
  public int GetSimilarityHash() {
    if (!similarityHash.HasValue) {
      similarityHash =
          (srcLog.source + srcLog.type + srcLog.message + srcLog.stackTrace).GetHashCode();
    }
    return similarityHash.Value;
  }

  /// <summary>Merges repeated log into an existing record.</summary>
  /// <remarks>
  /// Only does merging of ID and the timestamp. caller is responsible for updating other fields.
  /// </remarks>
  /// <param name="log">A log record to merge. This is a readonly parameter!</param>
  public void MergeRepeated(LogRecord log) {
    _lastId = log.srcLog.id;
    // Math.Max() won't work for DateTime.
    _timestamp = log._timestamp > _timestamp ? log.timestamp : _timestamp;
    ++mergedLogs;
  }

  /// <summary>Gives log's timestamp in a unified <see cref="TimestampFormat"/>.</summary>
  /// <returns>A human readable timestamp string.</returns>
  public string FormatTimestamp() {
    return _timestamp.ToString(TimestampFormat);
  }

  /// <summary>Returns a text form of the log.</summary>
  /// <remarks>Not supposed to have stack trace.</remarks>
  /// <returns>A string that describes the event.</returns>
  public string MakeTitle() {
    var titleBuilder = new StringBuilder(TitleMaxSize);
    titleBuilder.Append(FormatTimestamp()).Append(" [");
    // Not using a dict lookup to save performance.
    switch (srcLog.type) {
      case LogType.Log:
        titleBuilder.Append(InfoPrefix);
        break;
      case LogType.Warning:
        titleBuilder.Append(WarningPrefix);
        break;
      case LogType.Error:
        titleBuilder.Append(ErrorPrefix);
        break;
      case LogType.Exception:
        titleBuilder.Append(ExceptionPrefix);
        break;
      default:
        titleBuilder.Append(srcLog.type);
        break;
    }
    titleBuilder.Append("] ");
    if (mergedLogs > 1) {
      titleBuilder.Append('[').Append(RepeatedPrefix).Append(mergedLogs).Append("] ");
    }
    if (srcLog.source.Length > 0) {
      titleBuilder.Append('[').Append(srcLog.source).Append("] ");
    }
    titleBuilder.Append(srcLog.message);
    return titleBuilder.ToString();
  }

  /// <summary>Resolves the file paths on the stack trace records.</summary>
  /// <remarks>This method is not performance efficient.</remarks>
  public void ResolveStackFilenames() {
    if (srcLog.filenamesResolved) {
      return;  // Nothing to do.
    }
    var lines = srcLog.stackTrace.Split('\n');
    if (srcLog.stackFrames == null || lines.Length != srcLog.stackFrames.Length) {
      srcLog.filenamesResolved = true;  // Cannot resolve.
      return;
    }
    var gameRoot = Path.GetFullPath(new Uri(KSPUtil.ApplicationRootPath).LocalPath);
    var matches = new List<string>();
    for (var i = 0; i < lines.Length; i++) {
      var assembly = srcLog.stackFrames[i].GetMethod().DeclaringType.Assembly;
      var relativePath = new Uri(gameRoot)
          .MakeRelativeUri(new Uri(assembly.Location))
          .ToString()
          .Replace(Path.DirectorySeparatorChar, '/');
      matches.Add(string.Format(
          "{0} in {1} [v{2}]", lines[i], relativePath, assembly.GetName().Version));
    }
    srcLog.stackTrace = string.Join("\n", matches.ToArray());
    srcLog.filenamesResolved = true;
  }
}

} // namespace KSPDev
