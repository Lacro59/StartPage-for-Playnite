﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.ComponentModel;
using System.Collections.ObjectModel;
using Playnite.SDK;
using Playnite.SDK.Models;
using LandingPage.Models;
using System.Windows.Data;
using System.Windows;
using LandingPage.Extensions;
using System.Collections;
using System.Globalization;
using System.Windows.Threading;
using System.Diagnostics;

namespace LandingPage.ViewModels
{
    public class BackgroundQueueItem : ObservableObject
    {
        public BackgroundQueueItem(Uri uri, double opacity)
        {
            Uri = uri;
            Opacity = opacity;
            TTL = LandingPageExtension.Instance.Settings.AnimationDuration;
        }
        public const double MaxTTL = 0;
        internal Uri uri;
        public Uri Uri { get => uri; set => SetValue(ref uri, value); }
        internal double ttl = 0;
        public double TTL { get => ttl; set => SetValue(ref ttl, value); }
        internal double opacity = 0;
        public double Opacity { get => Math.Max(0, Math.Min(1, opacity)); set => SetValue(ref opacity, value); }
    }

    public class LandingPageViewModel : ObservableObject
    {
        internal ObservableCollection<GameModel> recentlyPlayedGames = new ObservableCollection<GameModel>();
        public ObservableCollection<GameModel> RecentlyPlayedGames => recentlyPlayedGames;

        internal ObservableCollection<GameModel> recentlyAddedGames = new ObservableCollection<GameModel>();
        public ObservableCollection<GameModel> RecentlyAddedGames => recentlyAddedGames;

        internal ObservableCollection<GameModel> favoriteGames = new ObservableCollection<GameModel>();
        public ObservableCollection<GameModel> FavoriteGames => favoriteGames;

        internal ObservableCollection<ShelveViewModel> shelveViewModels = new ObservableCollection<ShelveViewModel>();
        public ObservableCollection<ShelveViewModel> ShelveViewModels { get => shelveViewModels; set => SetValue(ref shelveViewModels, value); }

        internal Uri backgroundImagePath = null;
        public Uri BackgroundImagePath 
        {
            get => backgroundImagePath;
            set
            {
                SetValue(ref backgroundImagePath, value);
                if (BackgroundImageQueue.Count == 0)
                {
                    BackgroundImageQueue.Add(new BackgroundQueueItem(value, 1) { TTL = 0 });
                } 
                else if (BackgroundImageQueue.LastOrDefault()?.Uri != value)
                {
                    BackgroundImageQueue.Add(new BackgroundQueueItem(value, 0));
                }
            }
        }

        internal ObservableCollection<BackgroundQueueItem> backgroundImageQueue = new ObservableCollection<BackgroundQueueItem>();
        public ObservableCollection<BackgroundQueueItem> BackgroundImageQueue => backgroundImageQueue;

        internal GameModel backgroundSourceGame = null;
        public GameModel BackgroundSourceGame { get => backgroundSourceGame; set => SetValue(ref backgroundSourceGame, value); }

        internal ObservableCollection<GameGroup> specialGames = new ObservableCollection<GameGroup>();
        public ObservableCollection<GameGroup> SpecialGames => specialGames;

        internal ObservableCollection<NotificationMessage> notifications;
        public ObservableCollection<NotificationMessage> Notifications => notifications;

        internal ICommand deleteNotificationCommand;
        public ICommand DeleteNotificationCommand => deleteNotificationCommand;

        internal ICommand clearNotificationsCommand;
        public ICommand ClearNotificationsCommand => clearNotificationsCommand;

        internal LandingPageSettingsViewModel settings;
        public LandingPageSettingsViewModel Settings => settings;

        internal Game lastSelectedGame;
        public Game LastSelectedGame { get => lastSelectedGame; set { SetValue(ref lastSelectedGame, value); UpdateBackgroundImagePath(false); } }

        internal Game lastHoveredGame;
        public Game LastHoveredGame { get => lastHoveredGame; set { SetValue(ref lastHoveredGame, value); UpdateBackgroundImagePath(false); } }

        internal Game currentlyHoveredGame;
        public Game CurrentlyHoveredGame
        {
            get => currentlyHoveredGame;
            set
            {
                SetValue(ref currentlyHoveredGame, value);
                if (value is Game && LastHoveredGame != value)
                {
                    LastHoveredGame = value;
                }
            }
        }

        internal Clock clock = new Clock();
        public Clock Clock => clock;

        internal SuccessStory.SuccessStoryViewModel successStory;
        public SuccessStory.SuccessStoryViewModel SuccessStory => successStory;

        internal GameActivity.GameActivityViewModel gameActivity;
        public GameActivity.GameActivityViewModel GameActivity => gameActivity;

        internal bool languageSupportsVertical = false;
        public bool LanguageSupportsVertical { get => languageSupportsVertical; set => SetValue(ref languageSupportsVertical, value); }

        internal IPlayniteAPI playniteAPI;
        internal LandingPageExtension plugin;
        internal DispatcherTimer backgroundImageTimer;
        internal DispatcherTimer backgroundRefreshTimer;

        private static readonly Random rng = new Random();

        public ICommand OpenSettingsCommand => new RelayCommand(() => plugin.OpenSettingsView());

        public ICommand NextRandomBackgroundCommand { get; set; }

        public ICommand AddShelveCommand { get; set; }

        public ICommand RemoveShelveCommand { get; set; }

        public ICommand ExtendShelveCommand { get; set; }

        private static readonly HashSet<string> verticalLanguages = new HashSet<string> { "zh_CN", "zh_TW", "vi_VN", "ja_JP", "zh_CN", "ko_KR", };

        public LandingPageViewModel(IPlayniteAPI playniteAPI, LandingPageExtension landingPage,
                                    LandingPageSettingsViewModel settings,
                                    SuccessStory.SuccessStoryViewModel successStory,
                                    GameActivity.GameActivityViewModel gameActivity)
        {
            this.playniteAPI = playniteAPI;
            this.plugin = landingPage;
            this.settings = settings;
            this.successStory = successStory;
            this.gameActivity = gameActivity;
            notifications = playniteAPI.Notifications.Messages;
            notifications.CollectionChanged += (sender, args) => OnPropertyChanged(nameof(Notifications));
            deleteNotificationCommand = new RelayCommand<NotificationMessage>(sender => playniteAPI.Notifications.Remove(sender.Id));
            clearNotificationsCommand = new RelayCommand(() => playniteAPI.Notifications.RemoveAll());
            Settings.Settings.PropertyChanged += Settings_PropertyChanged;
            Settings.PropertyChanged += Settings_PropertyChanged1;
            clock.DayChanged += Clock_DayChanged;
            backgroundImageQueue.CollectionChanged += BackgroundImageQueue_CollectionChanged;
            NextRandomBackgroundCommand = new RelayCommand(() => 
            {
                UpdateBackgroundImagePath(true);
            }, () => (Settings.Settings.BackgroundImageSource == BackgroundImageSource.Random && !System.IO.File.Exists(Settings.Settings.BackgroundImagePath))
            || System.IO.Directory.Exists(Settings.Settings.BackgroundImagePath));
            languageSupportsVertical = verticalLanguages.Contains(playniteAPI.ApplicationSettings.Language);
            AddShelveCommand = new RelayCommand(() => 
            {
                ShelveViewModels.Add(new ShelveViewModel(ShelveProperties.RecentlyPlayed, playniteAPI));
            });
            RemoveShelveCommand = new RelayCommand<ShelveViewModel>(svm => {if (svm != null) ShelveViewModels.Remove(svm); });
            ExtendShelveCommand = new RelayCommand<ShelveViewModel>(svm =>
            {
                var idx = ShelveViewModels.IndexOf(svm);
                if (idx > -1)
                {
                    var properties = svm.ShelveProperties.Copy();
                    properties.SkippedGames = svm.ShelveProperties.NumberOfGames + svm.ShelveProperties.SkippedGames;
                    properties.Name = string.Empty;
                    ShelveViewModels.Insert(idx + 1, new ShelveViewModel(properties, playniteAPI));
                }
            });
            UpdateBackgroundTimer();
        }

        private void BackgroundImageQueue_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                if (BackgroundImageQueue.Count > 1)
                {
                    BackgroundImageQueue[BackgroundImageQueue.Count - 2].TTL = Settings.Settings.AnimationDuration - BackgroundImageQueue[BackgroundImageQueue.Count - 2].TTL;
                }
                if (backgroundImageTimer == null)
                {
                    backgroundImageTimer = new DispatcherTimer(DispatcherPriority.Render, Application.Current.Dispatcher);
                    backgroundImageTimer.Interval = TimeSpan.FromMilliseconds(16);
                    Stopwatch stopwatch = new Stopwatch();
                    backgroundImageTimer.Tick += (_, args) => 
                    {
                        double elapsedSeconds = backgroundImageTimer.Interval.TotalSeconds;
                        if (!stopwatch.IsRunning)
                        {
                            stopwatch.Start();
                        } else
                        {
                            elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                            stopwatch.Restart();
                        }
                        // Debug.WriteLine(string.Join("|", BackgroundImageQueue.Select(i => string.Format("TTL = {0}, Opacity = {1}", i.TTL, i.Opacity))));
                        if (BackgroundImageQueue.Count > 0)
                        {
                            BackgroundImageQueue.ForEach(item => item.TTL -= elapsedSeconds);
                        }
                        for(int i = 0; i < BackgroundImageQueue.Count; ++i)
                        {
                            if (i < BackgroundImageQueue.Count - 1)
                            {
                                BackgroundImageQueue[i].Opacity = BackgroundImageQueue[i].TTL / Settings.Settings.AnimationDuration;
                            } else
                            {
                                BackgroundImageQueue[i].Opacity = 1.0 - (BackgroundImageQueue[i].TTL / Settings.Settings.AnimationDuration);
                            }
                        }
                        for(int i = BackgroundImageQueue.Count - 2; i >= 0; --i)
                        {
                            if (BackgroundImageQueue[i].TTL <= 0)
                            {
                                BackgroundImageQueue.RemoveAt(i);
                            }
                        }
                        if (BackgroundImageQueue.Count == 1 && BackgroundImageQueue.Last().Opacity >= 1.0)
                        {
                            backgroundImageTimer.Stop();
                            stopwatch.Stop();
                            GC.Collect();
                        }
                    };
                }
                if (!backgroundImageTimer.IsEnabled && BackgroundImageQueue.Count > 1)
                {
                    backgroundImageTimer.Start();
                }
            }
        }

        private void Clock_DayChanged(object sender, EventArgs e)
        {
            
        }

        private void Settings_PropertyChanged1(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LandingPageSettingsViewModel.Settings))
            {
                UpdateBackgroundImagePath();
                UpdateBackgroundTimer();
                Settings.Settings.PropertyChanged += Settings_PropertyChanged;
            }
        }

        private void UpdateBackgroundTimer()
        {
            if (Settings.Settings.BackgroundRefreshInterval != 0)
            {
                if (backgroundRefreshTimer == null)
                {
                    backgroundRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher);
                    backgroundRefreshTimer.Tick += (s, e) => 
                    {
                        if (!playniteAPI.Database.Games.Any(g => g.IsRunning))
                        {
                            UpdateBackgroundImagePath(true);
                        }
                    };
                }
                if (backgroundRefreshTimer.Interval.TotalMinutes != Settings.Settings.BackgroundRefreshInterval)
                {
                    backgroundRefreshTimer.Stop();
                    backgroundRefreshTimer.Interval = TimeSpan.FromMinutes(Settings.Settings.BackgroundRefreshInterval);
                }
                if (!backgroundRefreshTimer.IsEnabled)
                {
                    backgroundRefreshTimer.Start();
                }
            }
            else
            {
                backgroundRefreshTimer?.Stop();
                backgroundRefreshTimer = null;
            }
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LandingPageSettings.BackgroundImageUri))
            {
                UpdateBackgroundImagePath();
            }
            if (e.PropertyName == nameof(LandingPageSettings.BackgroundImageSource))
            {
                UpdateBackgroundImagePath();
            }
            if (e.PropertyName == nameof(LandingPageSettings.BackgroundRefreshInterval))
            {
                UpdateBackgroundTimer();
            }
        }

        public void Subscribe()
        {
            GameActivity.Activities.CollectionChanged += Activities_CollectionChanged;
            playniteAPI.Database.Games.ItemUpdated += Games_ItemUpdated;
            playniteAPI.Database.Games.ItemCollectionChanged += Games_ItemCollectionChanged;
        }

        public void Unsubscribe()
        {
            GameActivity.Activities.CollectionChanged -= Activities_CollectionChanged;
            playniteAPI.Database.Games.ItemUpdated -= Games_ItemUpdated;
            playniteAPI.Database.Games.ItemCollectionChanged -= Games_ItemCollectionChanged;
        }

        private void Activities_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateMostPlayedGame();
        }

        public void Update(bool updateRandomBackground = true)
        {
            //UpdateRecentlyPlayedGames();
            //UpdateRecentlyAddedGames();
            ShelveViewModels.ForEach(m => m.UpdateGames(m.ShelveProperties));
            UpdateFavorites();
            UpdateBackgroundImagePath(updateRandomBackground);
            UpdateMostPlayedGame();
            successStory.Update();

        }

        private void UpdateMostPlayedGame()
        {
            var groups = new List<GameGroup>();
            
            var releaseDateComparer = new Func<Game, Game, int>((Game a, Game b) =>
            {
                if (a?.ReleaseDate == null)
                    return 1;
                if (b?.ReleaseDate == null)
                    return -1;
                return -a.ReleaseDate.Value.CompareTo(b.ReleaseDate.Value);
            });
            var thisWeek = GameActivity.Activities
                .Select(a => new { Game = playniteAPI.Database.Games.Get(a.Id), Items = a.Items.Where(i => i.DateSession.AddDays(7) >= DateTime.Today).ToList() })
                .Where(a => a.Game is Game && a.Items?.Count > 0);
            var mostPlayedThisWeek = thisWeek
                .Select(a => new { Game = a.Game, Playtime = a.Items.Sum(i => (long)i.ElapsedSeconds) })
                .MaxElement(g => g.Playtime).Value?.Game;
            var thisMonth = GameActivity.Activities
                .Select(a => new { Game = playniteAPI.Database.Games.Get(a.Id), Items = a.Items.Where(i => i.DateSession.AddDays(30) >= DateTime.Today).ToList() })
                .Where(a => a.Game is Game && a.Items?.Count > 0);
            var mostPlayedThisMonth = thisMonth
                .Select(a => new { Game = a.Game, Playtime = a.Items.Sum(i => (long)i.ElapsedSeconds) })
                .MaxElement(g => g.Playtime).Value?.Game;

            var lastPlayedComparer = new Func<Game, Game, int>((Game a, Game b) =>
            {
                if (a?.LastActivity == null)
                    return 1;
                if (b?.LastActivity == null)
                    return -1;
                return -a.LastActivity.Value.CompareTo(b.LastActivity.Value);
            });

            //if (playniteAPI.Database.Games.Where(g => !g.Hidden).MaxElement(releaseDateComparer) is Game newestGame)
            //{
            //    var group = new GameGroup();
            //    group.Games.Add(new GameModel(newestGame));
            //    group.Label = ResourceProvider.GetString("LOC_SPG_LatestReleaseGame");
            //    groups.Add(group);
            //}

            if (mostPlayedThisWeek is Game mostPlayedWeek)
            {
                var group = new GameGroup();
                group.Games.Add(new GameModel(mostPlayedWeek));
                group.Label = ResourceProvider.GetString("LOC_SPG_LastSevenDays");
                groups.Add(group);
            }

            if (mostPlayedThisMonth is Game mostPlayedMonth)
            {
                var group = new GameGroup();
                group.Games.Add(new GameModel(mostPlayedMonth));
                group.Label = ResourceProvider.GetString("LOC_SPG_LastThirtyDays");
                groups.Add(group);
            }

            if (playniteAPI.Database.Games.Where(g => !g.Hidden).MaxElement(g => g.Playtime).Value is Game game)
            {
                var group = new GameGroup();
                group.Games.Add(new GameModel(game));
                group.Label = ResourceProvider.GetString("LOC_SPG_MostPlayedGame");
                groups.Add(group);
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (specialGames.Count == 0)
                {
                    specialGames.Update(groups);
                } else
                {
                    foreach(var group in specialGames)
                    {
                        var currentGroup = groups.FirstOrDefault(g => g.Label == group.Label);
                        if (currentGroup?.Games?.FirstOrDefault()?.Game is Game updatedGame)
                        {
                            group.Games.FirstOrDefault().Game = updatedGame;
                        }
                    }
                }
            });
        }

        private void UpdateBackgroundImagePath(bool updateRandomBackground = true)
        {
            Game gameSource = null;
            Uri path = null;
            if (Settings.Settings.BackgroundImageUri is Uri)
            {
                path = Settings.Settings.BackgroundImageUri;
            }
            if (path == null)
            {
                if (System.IO.Directory.Exists(Settings.Settings.BackgroundImagePath))
                {
                    var imagePaths = System.IO.Directory.EnumerateFiles(Settings.Settings.BackgroundImagePath)
                        .Where(file => file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                        || file.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                        || file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        .Where(file => BackgroundImagePath?.LocalPath != file);
                    if (BackgroundImagePath?.LocalPath != null 
                        
                        && !updateRandomBackground)
                    {
                        path = BackgroundImagePath;
                    } 
                    else if(imagePaths.ElementAtOrDefault(rng.Next(imagePaths.Count())) is string imagePath)
                    {
                        if (Uri.TryCreate(imagePath, UriKind.RelativeOrAbsolute, out var uri))
                        {
                            path = uri;
                        }
                    }
                }
            }
            if (path == null)
            {
                switch (Settings.Settings.BackgroundImageSource)
                {
                    case BackgroundImageSource.LastPlayed:
                        {
                            var mostRecent = recentlyPlayedGames.OrderByDescending(g => g.Game.LastActivity)
                                                 .FirstOrDefault(g => !string.IsNullOrEmpty(g.Game.BackgroundImage));
                            var databasePath = mostRecent?.Game.BackgroundImage;
                            if (!string.IsNullOrEmpty(databasePath))
                            {
                                var fullPath = playniteAPI.Database.GetFullFilePath(databasePath);
                                if (Uri.TryCreate(fullPath, UriKind.RelativeOrAbsolute, out var uri))
                                {
                                    path = uri;
                                    gameSource = mostRecent.Game;
                                }
                            }
                            break;
                        }
                    case BackgroundImageSource.LastAdded:
                        {
                            var mostRecent = recentlyAddedGames.OrderByDescending(g => g.Game.Added)
                                                 .FirstOrDefault(g => !string.IsNullOrEmpty(g.Game.BackgroundImage));
                            var databasePath = mostRecent?.Game.BackgroundImage;
                            if (!string.IsNullOrEmpty(databasePath))
                            {
                                var fullPath = playniteAPI.Database.GetFullFilePath(databasePath);
                                if (Uri.TryCreate(fullPath, UriKind.RelativeOrAbsolute, out var uri))
                                {
                                    path = uri;
                                    gameSource = mostRecent.Game;
                                }
                            }
                            break;
                        }
                    case BackgroundImageSource.MostPlayed:
                        {
                            var mostPlayed = playniteAPI.Database.Games.MaxElement(game => game.Playtime);
                            if (mostPlayed.Value is Game)
                            {
                                var databasePath = mostPlayed.Value?.BackgroundImage;
                                if (!string.IsNullOrEmpty(databasePath))
                                {
                                    var fullPath = playniteAPI.Database.GetFullFilePath(databasePath);
                                    if (Uri.TryCreate(fullPath, UriKind.RelativeOrAbsolute, out var uri))
                                    {
                                        path = uri;
                                        gameSource = mostPlayed.Value;
                                    }
                                }
                            }
                            break;
                        }
                    case BackgroundImageSource.Random:
                        {
                            if (updateRandomBackground || BackgroundImagePath == null)
                            {
                                var candidates = playniteAPI.Database.Games
                                    .Where(game => !game.Hidden)
                                    .Where(game => !string.IsNullOrEmpty(game.BackgroundImage));
                                var randomGame = candidates.ElementAtOrDefault(rng.Next(candidates.Count()));
                                if (randomGame is Game)
                                {
                                    var databasePath = randomGame.BackgroundImage;
                                    if (!string.IsNullOrEmpty(databasePath))
                                    {
                                        var fullPath = playniteAPI.Database.GetFullFilePath(databasePath);
                                        if (Uri.TryCreate(fullPath, UriKind.RelativeOrAbsolute, out var uri))
                                        {
                                            path = uri;
                                            gameSource = randomGame;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                path = BackgroundImagePath;
                            }
                            break;
                        }
                    case BackgroundImageSource.LastSelected:
                        {
                            if (LastSelectedGame != null && LastSelectedGame.BackgroundImage != null)
                            {
                                var databasePath = LastSelectedGame.BackgroundImage;
                                if (!string.IsNullOrEmpty(databasePath))
                                {
                                    var fullPath = playniteAPI.Database.GetFullFilePath(databasePath);
                                    if (Uri.TryCreate(fullPath, UriKind.RelativeOrAbsolute, out var uri))
                                    {
                                        path = uri;
                                        gameSource = lastSelectedGame;
                                    }
                                }
                            }
                        }
                        break;
                    case BackgroundImageSource.LastHovered:
                        {
                            if (LastHoveredGame != null && LastHoveredGame.BackgroundImage != null)
                            {
                                var databasePath = LastHoveredGame.BackgroundImage;
                                if (!string.IsNullOrEmpty(databasePath))
                                {
                                    var fullPath = playniteAPI.Database.GetFullFilePath(databasePath);
                                    if (Uri.TryCreate(fullPath, UriKind.RelativeOrAbsolute, out var uri))
                                    {
                                        path = uri;
                                        gameSource = LastHoveredGame;
                                    }
                                }
                            }
                        }
                        break;
                }
            }
            if (path == null)
            {
                gameSource = recentlyPlayedGames.OrderByDescending(g => g.Game.LastActivity)
                    .Concat(recentlyAddedGames.OrderByDescending(g => g.Game.Added))
                    .FirstOrDefault(g => !string.IsNullOrEmpty(g.Game.BackgroundImage))?.Game;
                var databasePath = gameSource?.BackgroundImage;
                if (!string.IsNullOrEmpty(databasePath))
                {
                    var fullPath = playniteAPI.Database.GetFullFilePath(databasePath);
                    if (Uri.TryCreate(fullPath, UriKind.RelativeOrAbsolute, out var uri))
                    {
                        path = uri;
                    }
                }
            }
            if (path is Uri && path != BackgroundImagePath)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (backgroundRefreshTimer != null && backgroundRefreshTimer.IsEnabled)
                    {
                        backgroundRefreshTimer.Stop();
                        backgroundRefreshTimer.Start();
                    }
                    BackgroundSourceGame = gameSource != null ? new GameModel(gameSource) : null;
                    BackgroundImagePath = path;
                });
            }
        }

        GameModel dummy = new GameModel(new Game());

        internal void UpdateRecentlyPlayedGames()
        {
            var games = playniteAPI.Database.Games
                .Where(g => !g.Hidden)
                .OrderByDescending(g => g.LastActivity);
            var collection = recentlyPlayedGames;
            var changed = false;
            Application.Current.Dispatcher.Invoke(() =>
            {
                var displayedGames = Math.Min(Math.Max(15, games.Count(g => g.LastActivity?.CompareTo(DateTime.Today.AddDays(-7)) > 0)), 20);
                
                IEnumerable<Game> gameSelection = games.Take(Settings.Settings.NumberOfGames);
                foreach (var game in gameSelection)
                {
                    if (collection.FirstOrDefault(item => item.Game?.Id == game.Id) is GameModel model)
                    {
                        if (model.Game.LastActivity != game.LastActivity)
                        {
                            changed = true;
                        }
                        model.Game = game;
                    }
                    else if (collection.FirstOrDefault(item => gameSelection.All(s => s.Id != item.Game?.Id)) is GameModel unusedModel)
                    {
                        changed = true;
                        collection.Remove(unusedModel);
                        unusedModel.Game = game;
                        collection.Add(unusedModel);
                    }
                    else
                    {
                        changed = true;
                        collection.Add(new GameModel(game));
                    }
                }
                for (int j = collection.Count - 1; j >= 0; --j)
                {
                    if (gameSelection.All(g => g.Id != collection[j].Game?.Id))
                    {
                        changed = true;
                        collection.RemoveAt(j);
                    }
                }
                if (changed && collection.Count > 1)
                {
                    collection.Move(collection.Count - 1, 0);
                }
            });
        }

        internal void UpdateRecentlyAddedGames()
        {
            var games = playniteAPI.Database.Games
                .Where(g => !g.Hidden)
                .OrderByDescending(g => g.Added);
            var collection = recentlyAddedGames;
            var changed = false;
            Application.Current.Dispatcher.Invoke(() =>
            {
                var displayedGames = Math.Min(Math.Max(15, games.Count(g => g.Added?.CompareTo(DateTime.Today.AddDays(-7)) > 0)), 20);
                
                IEnumerable<Game> gameSelection = games.Take(Settings.Settings.NumberOfGames);
                foreach (var game in gameSelection)
                {
                    if (collection.FirstOrDefault(item => item.Game?.Id == game.Id) is GameModel model)
                    {
                        if (model.Game.Added != game.Added)
                        {
                            changed = true;
                        }
                        model.Game = game;
                    } else if (collection.FirstOrDefault(item => gameSelection.All(s => s.Id != item.Game?.Id)) is GameModel unusedModel)
                    {
                        changed = true;
                        collection.Remove(unusedModel);
                        unusedModel.Game = game;
                        collection.Add(unusedModel);
                    } else
                    {
                        changed = true;
                        collection.Add(new GameModel(game));
                    }
                }
                for (int j = collection.Count - 1; j >= 0; --j)
                {
                    if (gameSelection.All(g => g.Id != collection[j].Game?.Id))
                    {
                        changed = true;
                        collection.RemoveAt(j);
                    }
                }
                if (changed && collection.Count > 1)
                {
                    collection.Move(collection.Count - 1, 0);
                }
            });
        }

        internal void UpdateFavorites()
        {
            var games = playniteAPI.Database.Games
                .Where(g => !g.Hidden && g.Favorite)
                .OrderByDescending(g => g.LastActivity);
            var collection = favoriteGames;
            var changed = false;
            Application.Current.Dispatcher.Invoke(() =>
            {
                IEnumerable<Game> gameSelection = games.Take(Settings.Settings.NumberOfGames);
                foreach (var game in gameSelection)
                {
                    if (collection.FirstOrDefault(item => item.Game?.Id == game.Id) is GameModel model)
                    {
                        if (model.Game.LastActivity != game.LastActivity)
                        {
                            changed = true;
                        }
                        model.Game = game;
                    }
                    else if (collection.FirstOrDefault(item => gameSelection.All(s => s.Id != item.Game?.Id)) is GameModel unusedModel)
                    {
                        changed = true;
                        collection.Remove(unusedModel);
                        unusedModel.Game = game;
                        collection.Add(unusedModel);
                    }
                    else
                    {
                        changed = true;
                        collection.Add(new GameModel(game));
                    }
                }
                for (int j = collection.Count - 1; j >= 0; --j)
                {
                    if (gameSelection.All(g => g.Id != collection[j].Game?.Id))
                    {
                        changed = true;
                        collection.RemoveAt(j);
                    }
                }
                if (changed && collection.Count > 1)
                {
                    collection.Move(collection.Count - 1, 0);
                }
            });
        }

        private void Games_ItemCollectionChanged(object sender, ItemCollectionChangedEventArgs<Game> e)
        {
            if (e.RemovedItems.Count + e.AddedItems.Count > 0)
            {
                Update(false);
            }
        }

        private void Games_ItemUpdated(object sender, ItemUpdatedEventArgs<Game> e)
        {
            if (e.UpdatedItems.Any(u => IsRelevantUpdate(u)))
            {
                Update(false);
            }
        }

        private static bool IsRelevantUpdate(ItemUpdateEvent<Game> update)
        {
            var old = update.OldData;
            var updated = update.NewData;
            if (old.Name != updated.Name) return true;
            if (old.LastActivity != updated.LastActivity) return true;
            if (old.Added != updated.Added) return true;
            if (old.Playtime != updated.Playtime) return true;
            if (old.Hidden != updated.Hidden) return true;
            if (old.Favorite != updated.Favorite) return true;
            if (old.BackgroundImage != updated.BackgroundImage) return true;
            if (old.CoverImage != updated.CoverImage) return true;
            if (old.Icon != updated.Icon) return true;
            var oldPlatformIds = old.PlatformIds ?? new List<Guid>();
            var updatedPlatformIds = updated.PlatformIds ?? new List<Guid>();
            if (!(oldPlatformIds.All(id => updatedPlatformIds.Contains(id)) && oldPlatformIds.Count == updatedPlatformIds.Count)) return true;
            var oldFeatureIds = old.FeatureIds ?? new List<Guid>();
            var updatedFeatureIds = updated.FeatureIds ?? new List<Guid>();
            if (!(oldFeatureIds.All(id => updatedFeatureIds.Contains(id)) && oldFeatureIds.Count == updatedFeatureIds.Count)) return true;
            return false;
        }
    }
}
