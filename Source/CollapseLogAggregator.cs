﻿// Kerbal Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using KSPDev.ConfigUtils;
using System.Collections.Generic;
using System.Linq;

namespace KSPDev.LogConsole {

/// <summary>A log capturer that collapses last repeated records into one.</summary>
[PersistentFieldsFileAttribute("KSPDev/LogConsole/Plugins/PluginData/settings.cfg",
                               "CollapseLogAggregator")]
sealed class CollapseLogAggregator : BaseLogAggregator {
  /// <inheritdoc/>
  public override IEnumerable<LogRecord> GetLogRecords() {
    return logRecords.ToArray().Reverse();
  }
  
  /// <inheritdoc/>
  public override void ClearAllLogs() {
    logRecords.Clear();
    ResetLogCounters();
  }
  
  /// <inheritdoc/>
  protected override void DropAggregatedLogRecord(LinkedListNode<LogRecord> node) {
    logRecords.Remove(node);
    UpdateLogCounter(node.Value, -1);
  }

  /// <inheritdoc/>
  protected override void AggregateLogRecord(LogRecord logRecord) {
    if (logRecords.Any()
        && logRecords.Last().GetSimilarityHash() == logRecord.GetSimilarityHash()) {
      logRecords.Last().MergeRepeated(logRecord);
    } else {
      logRecords.AddLast(new LogRecord(logRecord));
      UpdateLogCounter(logRecord, 1);
    }
  }
}

} // namespace KSPDev
