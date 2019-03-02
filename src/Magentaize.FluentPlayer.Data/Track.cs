﻿using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Magentaize.FluentPlayer.Data.Extensions;

namespace Magentaize.FluentPlayer.Data
{
    public class Track
    {
        [Key]
        public long Id { get; set; }

        public Artist Artist { get; set; }

        public string Genres { get; set; }

        public Album Album { get; set; }

        public string Path { get; set; }

        public string SafePath { get; set; }

        public string FileName { get; set; }

        public string MimeType { get; set; }

        public long? FileSize { get; set; }

        public long? BitRate { get; set; }

        public long? SampleRate { get; set; }

        public string TrackTitle { get; set; }

        public long? TrackNumber { get; set; }

        public long? TrackCount { get; set; }

        public long? DiscNumber { get; set; }

        public long? DiscCount { get; set; }

        public TimeSpan? Duration { get; set; }

        public long? Year { get; set; }

        public long? HasLyrics { get; set; }

        public long DateAdded { get; set; }

        public long DateFileCreated { get; set; }

        public long DateLastSynced { get; set; }

        public long DateFileModified { get; set; }

        public long? NeedsIndexing { get; set; }

        public long? NeedsAlbumArtworkIndexing { get; set; }

        public long? IndexingSuccess { get; set; }

        public string IndexingFailureReason { get; set; }

        public long? Rating { get; set; }

        public long? Love { get; set; }

        public long? PlayCount { get; set; }

        public long? SkipCount { get; set; }

        public long? DateLastPlayed { get; set; }

        public Track ShallowCopy()
        {
            return (Track)MemberwiseClone();
        }
    }
}