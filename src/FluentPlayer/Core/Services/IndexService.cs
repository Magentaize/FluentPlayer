﻿using DynamicData;
using Magentaize.FluentPlayer.Core.Storage;
using Magentaize.FluentPlayer.Data;
using Microsoft.EntityFrameworkCore;
using ReactiveUI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using TagLib;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Magentaize.FluentPlayer.Core.Services
{
    public class IndexService
    {
        private readonly ISourceList<Track> _trackSource = new SourceList<Track>();

        public IObservable<IChangeSet<Track>> TrackSource => _trackSource.Connect();

        private readonly ISourceList<Artist> _artistSource = new SourceList<Artist>();

        public IObservable<IChangeSet<Artist>> ArtistSource => _artistSource.Connect();

        private readonly ISourceCache<Album, long> _albumSource = new SourceCache<Album, long>(x => x.Id);

        public IObservable<IChangeSet<Album, long>> AlbumSource => _albumSource.Connect();

        private readonly ObservableCollection<StorageFolder> _musicFolders = new ObservableCollection<StorageFolder>();

        public ReadOnlyObservableCollection<StorageFolder> MusicFolders { get; private set; }

        public event EventHandler IndexBegin;
        public event EventHandler IndexProgressChanged;
        public event EventHandler IndexFinished;

        public int QueueIndexingCount { get; private set; }
        public int QueueIndexedCount { get; private set; }

        private StorageLibrary _musicLibrary;

        internal IndexService()
        {
            
        }

        private async Task<(IEnumerable<Folder> removedFolders, IEnumerable<StorageFolder> changedFolders)> GetChangedLibraryFolders()
        {
            var dbFolders = await ServiceFacade.Db.Folders.ToListAsync();
            var removedFolders = dbFolders.Except(_musicFolders, a => a.Path, b => b.Path).ToList();

            dbFolders.RemoveAll(x => removedFolders.Contains(x));
            var musicFoldersMixin = await Task.WhenAll(
                _musicFolders.Select(async x =>
                {
                    var prop = await x.GetBasicPropertiesAsync();
                    return new { Obj = x, Prop = prop };
                }));

            var indexNeededFolders = musicFoldersMixin.Where(x =>
            {
                var f = dbFolders.FirstOrDefault(y => y.Path == x.Obj.Path);
                if (f == default(Folder)) return true;
                if (f.ModifiedDate != x.Prop.DateModified) return true;
                return false;
            }).Select(x => x.Obj);

            return (removedFolders, indexNeededFolders);
        }

        private async Task IndexChangedFolders(IEnumerable<StorageFolder> folders)
        {
            var libFiles = (await folders.SelectManyAsync<StorageFolder, StorageFile>(async f =>
               await StorageFolderQuery.Create(f, StorageFolderQuery.AudioExtensions).ExecuteQueryAsync())).ToList();
            var dbTracks = await ServiceFacade.Db.Tracks.ToListAsync();
            var commonPath = libFiles.Intersect(dbTracks, a => a.Path, b => b.Path).ToHashSet();

            var newFiles = libFiles.Except(commonPath, f => f.Path).ToArray();
            var deletedTracks = dbTracks.Except(commonPath, t => t.Path);

            // if there are some files need to be indexed, query cover pictures in library
            if (newFiles.Count() != 0)
            {
                var pics = (await folders.SelectManyAsync<StorageFolder, StorageFile>(async f =>
                    await StorageFolderQuery.Create(f, StorageFolderQuery.AlbumCoverExtensions).ExecuteQueryAsync())).ToList();
                _albumCoverList = pics.ToDictionary(p => Path.GetDirectoryName(p.Path));
            }

            foreach (var file in newFiles)
            {
                await IndexFileAsync(file);
            }

            await RemoveIndexedFileAsync(deletedTracks);
        }

        private async Task IndexRemovedFolders(IEnumerable<Folder> folders)
        {

        }

        public async Task BeginIndexAsync()
        {
            var (r, i) = await GetChangedLibraryFolders();
            await IndexRemovedFolders(r);
            await IndexChangedFolders(i);
        }

        internal async Task<IndexService> InitializeAsync()
        {
            MusicFolders = new ReadOnlyObservableCollection<StorageFolder>(_musicFolders);
            _musicLibrary = await StorageLibrary.GetLibraryAsync(KnownLibraryId.Music);
            _musicFolders.AddRange(_musicLibrary.Folders);
            _musicLibrary.DefinitionChanged += _musicLibrary_DefinitionChanged;

            await ServiceFacade.Db.Tracks.Include(t => t.Album).Include(t => t.Artist)
                .ToAsyncEnumerable()
                .ForEachAsync(x => _trackSource.Add(x));

            await ServiceFacade.Db.Artists.Include(a => a.Tracks).Include(a => a.Albums)
                .ToAsyncEnumerable()
                .ForEachAsync(x => _artistSource.Add(x));

            await ServiceFacade.Db.Albums.Include(a => a.Tracks).Include(a => a.Artist)
                .ToAsyncEnumerable()
                .ForEachAsync(x => _albumSource.AddOrUpdate(x));

            return this;
        }

        private static void _musicLibrary_DefinitionChanged(StorageLibrary sender, object args)
        {
            
        }

        private IDictionary<string, StorageFile> _albumCoverList;

        public async Task BeginIndexAsync2()
        {
            var list = new List<StorageFolder> {KnownFolders.MusicLibrary};

            var libAudioFiles = (await list.SelectManyAsync<StorageFolder, StorageFile>(async f =>
                await StorageFolderQuery.Create(f, StorageFolderQuery.AudioExtensions).ExecuteQueryAsync())).ToList();
            var filesPath = libAudioFiles.Select(f => f.Path).ToArray();
            var dbTracks = await ServiceFacade.Db.Tracks.ToListAsync();
            var dbTracksPath = dbTracks.Select(t => t.Path).ToArray();

            var commonPath = filesPath.Intersect(dbTracksPath).ToArray();

            var newFiles = libAudioFiles.Where(f => !commonPath.Contains(f.Path)).ToArray();
            var deletedTracks = dbTracks.Where(t => !commonPath.Contains(t.Path));

            // begin index
            QueueIndexingCount = newFiles.Count();
            QueueIndexedCount = 0;
            IndexBegin?.Invoke(this, null);

            // if there are some files need to be indexed, query cover pictures in library
            if (newFiles.Count() != 0)
            {
                var pics = (await list.SelectManyAsync<StorageFolder, StorageFile>(async f =>
                    await StorageFolderQuery.Create(f, StorageFolderQuery.AlbumCoverExtensions).ExecuteQueryAsync())).ToList();
                _albumCoverList = pics.ToDictionary(p => Path.GetDirectoryName(p.Path));
            }

            foreach (var file in newFiles)
            {
                await IndexFileAsync(file);
                QueueIndexedCount++;
                IndexProgressChanged?.Invoke(this, null);
            }

            await RemoveIndexedFileAsync(deletedTracks);

            IndexFinished?.Invoke(this, null);
        }

        //remove deleted tracks in database
        private async Task RemoveIndexedFileAsync(IEnumerable<Track> tracks)
        {
            var group = tracks.GroupBy(t => t.Artist).GroupBy(at => at.Key.Albums);

            ServiceFacade.Db.Tracks.RemoveRange(tracks);
            await ServiceFacade.Db.SaveChangesAsync();

            foreach(var artist in group)
            {

            }
        }

        // search for existed artist
        private async Task<(bool, Artist)> FindOrCreateArtist(Tag tag)
        {
            var artistName = tag.FirstPerformer ?? "Unknown";
            var artist = await ServiceFacade.Db.Artists.FirstOrDefaultAsync(a => a.Name == artistName);
            if (artist == null)
            {
                artist = new Artist()
                {
                    Name = tag.FirstPerformer ?? "Unknown",
                };

                await ServiceFacade.Db.Artists.AddAsync(artist);
                return (true, artist);
            }

            return (false, artist);
        }

        // search for existed album
        private async Task<(bool, Album)> FindOrCreateAlbum(Tag tag, string path)
        {
            var albumTitle = tag.Album ?? "Unknown";
            var album = await ServiceFacade.Db.Albums.FirstOrDefaultAsync(a => a.Title == albumTitle);
            if (album == null)
            {
                album = new Album()
                {
                    Title = albumTitle,
                    AlbumCover = tag.FirstAlbumArtist ?? "Unknown",
                };

                IBuffer picData = null;
                if (tag.Pictures != null && tag.Pictures.Length >= 1)
                {
                    picData = tag.Pictures[0].Data.Data.AsBuffer();
                }
                else if (_albumCoverList.TryGetValue(Path.GetDirectoryName(path), out var pic))
                {
                    picData = await FileIO.ReadBufferAsync(pic);
                }

                album.AlbumCover = picData == null ? default : await ServiceFacade.CacheService.CacheAsync(picData);

                return (true, album);
            }

            return (false, album);
        }

        private async Task IndexFileAsync(IStorageFile file)
        {
            var path = file.Path;
            var tFile = TagLib.File.Create(await UwpFileAbstraction.CreateAsync(file));

            var prop = tFile.Properties;
            var tag = tFile.Tag;

            (var albumCreated, var album) = await FindOrCreateAlbum(tag, path);
            (var artistCreated, var artist) = await FindOrCreateArtist(tag);

            if (artistCreated)
            {
                _artistSource.Add(artist);
                artist.Albums.Add(album);
            }

            if (albumCreated)
            {
                _albumSource.AddOrUpdate(album);
                album.Artist = artist;
            }

            var fbp = await file.GetBasicPropertiesAsync();

            var track = new Track()
            {
                Path = path,
                FileName = Path.GetFileNameWithoutExtension(path),
                IndexingSuccess = 0,
                DateAdded = DateTime.Now.Ticks,
                TrackTitle = tag.Title,
                Year = tag.Year,
                Duration = prop.Duration,
                Artist = artist,
                Genres = tag.FirstGenre ?? "Unknown",
                FileSize = fbp.Size
            };

            artist.Tracks.Add(track);
            album.Tracks.Add(track);

            await ServiceFacade.Db.Tracks.AddAsync(track);
            _trackSource.Add(track);

            await ServiceFacade.Db.SaveChangesAsync();
        }

        public async Task RequestRemoveFolderAsync(StorageFolder folder)
        {
            var result = await _musicLibrary.RequestRemoveFolderAsync(folder);
            if (result)
            {
                _musicFolders.Remove(folder);
            }
        }

        public async Task RequestAddFolderAsync()
        {
            var f = await _musicLibrary.RequestAddFolderAsync();
            if (f != null)
            {
                _musicFolders.Add(f);
            }
        }
    }
}