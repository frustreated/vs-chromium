﻿// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Linq;
using VsChromium.Core.Ipc.TypedMessages;
using VsChromium.Core.Utility;
using VsChromium.Server.FileSystemNames;
using VsChromium.Server.NativeInterop;
using VsChromium.Server.Search;

namespace VsChromium.Server.FileSystemContents {
  /// <summary>
  /// Abstraction over a file contents
  /// </summary>
  public abstract class FileContents {
    protected const int MaxLineExtentOffset = 1024;

    protected static List<FilePositionSpan> NoSpans = new List<FilePositionSpan>();
    protected static IEnumerable<FileExtract> NoFileExtracts = Enumerable.Empty<FileExtract>();
    private readonly DateTime _utcLastModified;

    protected FileContents(DateTime utcLastModified) {
      _utcLastModified = utcLastModified;
    }

    public DateTime UtcLastModified { get { return _utcLastModified; } }

    public abstract long ByteLength { get; }

    public TextRange TextRange { get { return new TextRange(0, CharacterCount); } }

    public abstract bool HasSameContents(FileContents other);

    public FileContentsPiece CreatePiece(FileName fileName, int fileId, TextRange range) {
      return new FileContentsPiece(fileName, this, fileId, range);
    }

    /// <summary>
    /// Find all instances of the search pattern stored in <paramref
    /// name="compiledTextSearchData"/> within the passed in <paramref
    /// name="textRange"/>
    /// </summary>
    public List<FilePositionSpan> FindAll(
      CompiledTextSearchData compiledTextSearchData,
      TextRange textRange,
      IOperationProgressTracker progressTracker) {

      var textFragment = CreateFragmentFromRange(textRange);
      var providerForMainEntry = compiledTextSearchData
        .GetSearchAlgorithmProvider(compiledTextSearchData.ParsedSearchString.MainEntry);
      var textSearch = this.GetCompiledTextSearch(providerForMainEntry);
      var result = textSearch.FindAll(textFragment, progressTracker);
      if (compiledTextSearchData.ParsedSearchString.EntriesBeforeMainEntry.Count == 0 &&
          compiledTextSearchData.ParsedSearchString.EntriesAfterMainEntry.Count == 0) {
        return result.ToList();
      }

      return FilterOnOtherEntries(compiledTextSearchData, result).ToList();
    }

    public virtual IEnumerable<FileExtract> GetFileExtracts(IEnumerable<FilePositionSpan> spans) {
      return NoFileExtracts;
    }

    protected abstract long CharacterCount { get; }

    protected abstract int CharacterSize { get; }

    protected abstract TextFragment TextFragment { get; }

    protected abstract ICompiledTextSearch GetCompiledTextSearch(ICompiledTextSearchProvider provider);

    protected abstract TextRange GetLineTextRangeFromPosition(long position, long maxRangeLength);

    private TextFragment CreateFragmentFromRange(TextRange textRange) {
      // Note: In some case, textRange may be outside of our bounds. This is
      // because FileContents and FileContentsPiece may be out of date wrt to
      // each other, see FileData.UpdateContents method.
      var fullFragment = this.TextFragment;
      var offset = Math.Min(textRange.CharacterOffset, fullFragment.CharacterCount);
      var count = Math.Min(textRange.CharacterCount, fullFragment.CharacterCount - offset);
      var textFragment = this.TextFragment.Sub(offset, count);
      return textFragment;
    }

    private IEnumerable<FilePositionSpan> FilterOnOtherEntries(CompiledTextSearchData compiledTextSearchData, IEnumerable<FilePositionSpan> matches) {
      FindEntryFunction findEntry = (textRange, entry) => {
        var algo = this.GetCompiledTextSearch(compiledTextSearchData.GetSearchAlgorithmProvider(entry));
        var position = algo.FindFirst(CreateFragmentFromRange(textRange), OperationProgressTracker.None);
        if (!position.HasValue)
          return null;
        return new TextRange(position.Value.Position, position.Value.Length);
      };
      GetLineRangeFunction getLineRange = position => this.GetLineTextRangeFromPosition(position, MaxLineExtentOffset);

      return new TextSourceTextSearch(getLineRange, findEntry)
          .FilterOnOtherEntries(compiledTextSearchData.ParsedSearchString, matches);
    }
  }
}
