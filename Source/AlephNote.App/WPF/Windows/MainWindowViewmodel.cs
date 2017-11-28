﻿using AlephNote.Common.Settings.Types;
using AlephNote.Common.SPSParser;
using AlephNote.PluginInterface;
using AlephNote.Plugins;
using AlephNote.Repository;
using AlephNote.Settings;
using AlephNote.WPF.MVVM;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace AlephNote.WPF.Windows
{
	public class MainWindowViewmodel : ObservableObject, ISynchronizationFeedback
	{
		public ICommand SettingsCommand { get { return new RelayCommand(ShowSettings); } }
		public ICommand CreateNewNoteCommand { get { return new RelayCommand(CreateNote);} }
		public ICommand CreateNewNoteFromClipboardCommand { get { return new RelayCommand(CreateNoteFromClipboard);} }
		public ICommand ResyncCommand { get { return new RelayCommand(Resync); } }
		public ICommand ShowMainWindowCommand { get { return new RelayCommand(ShowMainWindow); } }
		public ICommand ExportCommand { get { return new RelayCommand(ExportNote); } }
		public ICommand DeleteCommand { get { return new RelayCommand(DeleteNote); } }
		public ICommand ExitCommand { get { return new RelayCommand(Exit); } }
		public ICommand ShowAboutCommand { get { return new RelayCommand(ShowAbout); } }
		public ICommand ShowLogCommand { get { return new RelayCommand(ShowLog); } }
		public ICommand SaveAndSyncCommand { get { return new RelayCommand(SaveAndSync); } }
		public ICommand DocumentSearchCommand { get { return new RelayCommand(ShowDocSearchBar); } }
		public ICommand CloseDocumentSearchCommand { get { return new RelayCommand(HideDocSearchBar); } }
		public ICommand FullResyncCommand { get { return new RelayCommand(FullResync); } }
		public ICommand ManuallyCheckForUpdatesCommand { get { return new RelayCommand(ManuallyCheckForUpdates); } }
		public ICommand DebugCreateIpsumNotesCommand { get { return new RelayCommand(DebugCreateIpsumNotes); } }
		public ICommand InsertSnippetCommand { get { return new RelayCommand<string>(InsertSnippet); } }

		public ICommand ClosingEvent { get { return new RelayCommand<CancelEventArgs>(OnClosing); } }
		public ICommand CloseEvent { get { return new RelayCommand<EventArgs>(OnClose); } }
		public ICommand StateChangedEvent { get { return new RelayCommand<EventArgs>(OnStateChanged); } }

		public ICommand SettingAlwaysOnTopCommand { get { return new RelayCommand(ChangeSettingAlwaysOnTop); } }
		public ICommand SettingLineNumbersCommand { get { return new RelayCommand(ChangeSettingLineNumbers); } }
		public ICommand SettingsWordWrapCommand   { get { return new RelayCommand(ChangeSettingWordWrap); } }
		
		private AppSettings _settings;
		public AppSettings Settings { get { return _settings; } private set { _settings = value; OnPropertyChanged(); } }

		private NoteRepository _repository;
		public NoteRepository Repository { get { return _repository; } private set { _repository = value; OnPropertyChanged(); OnExplicitPropertyChanged("NotesView"); } }

		private INote _selectedNote;
		public INote SelectedNote { get { return _selectedNote; } set { if (_selectedNote != value) { _selectedNote = value; OnPropertyChanged(); SelectedNoteChanged();} } }

		private DateTimeOffset? _lastSynchronized = null;

		private string _lastSynchronizedText = "never";
		public string LastSynchronizedText { get { return _lastSynchronizedText; } set { _lastSynchronizedText = value; OnPropertyChanged(); } }

		private string _searchText = string.Empty;
		public string SearchText { get { return _searchText; } set { if (_searchText != value) { _searchText = value; OnPropertyChanged(); FilterNoteList();} } }

		private WindowState _windowState = WindowState.Normal;
		public WindowState WindowState { get { return _windowState; } set { _windowState = value; OnPropertyChanged(); } }

		private SynchronizationState _synchronizationState = SynchronizationState.UpToDate;
		public SynchronizationState SynchronizationState { get { return _synchronizationState; } set { if (value != _synchronizationState) { _synchronizationState = value; OnPropertyChanged(); } } }

		public bool DebugMode { get { return App.DebugMode; } }

		public ListCollectionView NotesView
		{
			get
			{

				if (Repository == null) return (ListCollectionView)CollectionViewSource.GetDefaultView(new List<INote>());

				var source = (ListCollectionView)CollectionViewSource.GetDefaultView(Repository.Notes);
				source.Filter = p => SearchFilter((INote)p);
				if (Settings.NoteSorting != SortingMode.None) source.CustomSort = Settings.GetNoteComparator();
				return source;
			}
		}

		private GridLength _overviewGridLength = new GridLength(0);
		public GridLength OverviewListWidth { get { return _overviewGridLength; } set { if (value != _overviewGridLength) { _overviewGridLength = value; OnPropertyChanged(); GridSplitterChanged(); } } }

		public string FullVersion { get { return "AlephNote v" + App.APP_VERSION; } }

		private readonly SynchronizationDispatcher dispatcher = new SynchronizationDispatcher();
		private readonly DelayedCombiningInvoker _invSaveSettings;
		private readonly SimpleParamStringParser _spsParser = new SimpleParamStringParser();

		private bool _preventScintillaFocus = false;
		private bool _forceClose = false;

		public readonly MainWindow Owner;
		
		public MainWindowViewmodel(AppSettings settings, MainWindow parent)
		{
			Owner = parent;

			_settings = settings;
			_invSaveSettings = DelayedCombiningInvoker.Create(() => Application.Current.Dispatcher.BeginInvoke(new Action(SaveSettings)), 8 * 1000, 60 * 1000);

			_repository = new NoteRepository(App.PATH_LOCALDB, this, settings, settings.ActiveAccount, App.Logger, dispatcher);
			Repository.Init();
			
			Owner.TrayIcon.Visibility = (Settings.CloseToTray || Settings.MinimizeToTray) ? Visibility.Visible : Visibility.Collapsed;

			if (_settings.LastSelectedNote != null) SelectedNote = NotesView.OfType<INote>().FirstOrDefault(n => n.GetUniqueName() == _settings.LastSelectedNote);
			if (SelectedNote == null ) SelectedNote = NotesView.FirstOrDefault<INote>();

			OverviewListWidth = new GridLength(settings.OverviewListWidth);

			if (settings.CheckForUpdates)
			{
				var t = new Thread(CheckForUpdatesAsync) { Name = "UPDATE_CHECK" };
				t.Start();
			}
		}

		private void ShowSettings()
		{
			var registryKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

			Settings.LaunchOnBoot = registryKey != null && registryKey.GetValue(App.APPNAME_REG) != null;

			new SettingsWindow(this, Settings) {Owner = Owner}.ShowDialog();
		}

		private void CreateNote()
		{
			try
			{
				if (Owner.Visibility == Visibility.Hidden) ShowMainWindow();
				SelectedNote = Repository.CreateNewNote();
			}
			catch (Exception e)
			{
				ExceptionDialog.Show(Owner, "Cannot create note", e);
			}
		}

		public void ChangeSettings(AppSettings newSettings)
		{
			try
			{
				var reconnectRepo = !Settings.ActiveAccount.IsEqual(newSettings.ActiveAccount);

				if (reconnectRepo)
				{
					try
					{
						_repository.Shutdown();
					}
					catch (Exception e)
					{
						App.Logger.Error("Main", "Shutting down current connection failed", e);
						ExceptionDialog.Show(Owner, "Shutting down current connection failed.\r\nConnection will be forcefully aborted", e);
						_repository.KillThread();
					}
				}

				Settings = newSettings;
				Settings.Save();

				if (reconnectRepo)
				{
					_repository = new NoteRepository(App.PATH_LOCALDB, this, Settings, Settings.ActiveAccount, App.Logger, dispatcher);
					_repository.Init();

					OnExplicitPropertyChanged("Repository");

					SelectedNote = NotesView.FirstOrDefault<INote>();
					OnExplicitPropertyChanged("NotesView");
				}
				else
				{
					_repository.ReplaceSettings(Settings);
				}

				Owner.TrayIcon.Visibility = (Settings.CloseToTray || Settings.MinimizeToTray) ? Visibility.Visible : Visibility.Collapsed;

				if (Settings.LaunchOnBoot)
				{
					var registryKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
					if (registryKey != null) registryKey.SetValue(App.APPNAME_REG, App.PATH_EXECUTABLE);
				}
				else
				{
					var registryKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
					if (registryKey != null && registryKey.GetValue(App.APPNAME_REG) != null) registryKey.DeleteValue(App.APPNAME_REG);
				}

				Owner.SetupScintilla(Settings);

				SearchText = string.Empty;
			}
			catch (Exception e)
			{
				App.Logger.Error("Main", "Apply Settings failed", e);
				ExceptionDialog.Show(Owner, "Apply Settings failed.\r\nSettings and Local notes could be in an invalid state.\r\nContinue with caution.", e);
			}
		}

		private void SelectedNoteChanged()
		{
			Owner.ResetScintillaScrollAndUndo();
			Owner.UpdateMargins(Settings);
			if (!_preventScintillaFocus) Owner.FocusScintillaDelayed();

			Settings.LastSelectedNote = SelectedNote?.GetUniqueName();
			RequestSettingsSave();
		}

		public void OnNoteChanged(NoteChangedEventArgs e)
		{
			if (NotesView.FirstOrDefault<INote>() != e.Note) NotesView.Refresh();
		}

		private void GridSplitterChanged()
		{
			Settings.OverviewListWidth = OverviewListWidth.Value;
			RequestSettingsSave();
		}

		private void Resync()
		{
			Repository.SyncNow();
		}

		public void StartSync()
		{
			LastSynchronizedText = "[SYNCING]";
			SynchronizationState = SynchronizationState.Syncing;
		}

		public void SyncSuccess(DateTimeOffset now)
		{
			_lastSynchronized = now;
			LastSynchronizedText = now.ToLocalTime().ToString("HH:mm:ss");
			SynchronizationState = SynchronizationState.UpToDate;
		}

		public void OnSyncRequest()
		{
			SynchronizationState = SynchronizationState.NotSynced;
		}

		public void SyncError(List<Tuple<string, Exception>> errors)
		{
			if (_lastSynchronized != null)
			{
				LastSynchronizedText = _lastSynchronized.Value.ToLocalTime().ToString("HH:mm:ss");
			}
			else
			{
				LastSynchronizedText = "[ERROR]";
			}

			SynchronizationState = SynchronizationState.Error;

			App.Logger.Error("Sync", string.Join(Environment.NewLine, errors.Select(p => p.Item1)), string.Join("\r\n\r\n\r\n", errors.Select(p => p.Item2.ToString())));

			if (Owner.Visibility == Visibility.Hidden)
			{
				Owner.TrayIcon.ShowBalloonTip(
					"Synchronization failed", 
					string.Join(Environment.NewLine, errors.Select(p => p.Item1)), 
					BalloonIcon.Error);
			}
			else
			{
				SyncErrorDialog.Show(Owner, errors.Select(p => p.Item1), errors.Select(p => p.Item2));
			}

		}

		private void ShowMainWindow()
		{
			Owner.Show();
			WindowState = WindowState.Normal;
			Owner.Activate();
			Owner.Focus();
			Owner.FocusScintillaDelayed(150);
		}

		private void OnClosing(CancelEventArgs e)
		{
			if (Settings.CloseToTray && !_forceClose)
			{
				Owner.Hide();
				e.Cancel = true;
			}
		}

		private void OnClose(EventArgs args)
		{
			try
			{
				_repository.Shutdown();
			}
			catch (Exception e)
			{
				App.Logger.Error("Main", "Shutting down connection failed", e);
				ExceptionDialog.Show(Owner, "Shutting down connection failed.\r\nConnection will be forcefully aborted.", e);
				_repository.KillThread();
			}


			Repository.Shutdown();

			if (_invSaveSettings.HasPendingRequests())
			{
				_invSaveSettings.CancelPendingRequests();
				SaveSettings();
			}
		}

		private void OnStateChanged(EventArgs e)
		{
			if (WindowState == WindowState.Minimized && Settings.MinimizeToTray)
			{
				Owner.Hide();
			}
		}

		private void ExportNote()
		{
			if (SelectedNote == null) return;

			SaveFileDialog sfd = new SaveFileDialog();

			if (SelectedNote.HasTagCasInsensitive(AppSettings.TAG_MARKDOWN))
			{
				sfd.Filter = "Markdown files (*.md)|*.md";
				sfd.FileName = SelectedNote.Title + ".md";
			}
			else
			{
				sfd.Filter = "Text files (*.txt)|*.txt";
				sfd.FileName = SelectedNote.Title + ".txt";
			}

			if (sfd.ShowDialog() == true)
			{
				try
				{
					File.WriteAllText(sfd.FileName, SelectedNote.Text, Encoding.UTF8);
				}
				catch (Exception e)
				{
					App.Logger.Error("Main", "Could not write to file", e);
					ExceptionDialog.Show(Owner, "Could not write to file", e);
				}
			}
		}

		private void DeleteNote()
		{
			try
			{
				if (SelectedNote == null) return;

				if (MessageBox.Show(Owner, "Do you really want to delete this note?", "Delete note ?", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

				Repository.DeleteNote(SelectedNote, true);

				SelectedNote = NotesView.FirstOrDefault<INote>();
			}
			catch (Exception e)
			{
				App.Logger.Error("Main", "Could not delete note", e);
				ExceptionDialog.Show(Owner, "Could not delete note", e);
			}
		}

		public void Exit()
		{
			_forceClose = true;
			Owner.Close();
		}

		private void ShowAbout()
		{
			new AboutWindow{ Owner = Owner }.ShowDialog();
		}

		private void ShowLog()
		{
			new LogWindow { Owner = Owner }.Show();
		}

		private void SaveAndSync()
		{
			try
			{
				Repository.SaveAll();
				Repository.SyncNow();
			}
			catch (Exception e)
			{
				App.Logger.Error("Main", "Synchronization failed", e);
				ExceptionDialog.Show(Owner, "Synchronization failed", e);
			}
		}

		private void FullResync()
		{
			try
			{
				if (MessageBox.Show(Owner, "Do you really want to delete all local data and download the server data?", "Full resync?", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

				Repository.Shutdown(false);

				Repository.DeleteLocalData();

				_repository = new NoteRepository(App.PATH_LOCALDB, this, Settings, Settings.ActiveAccount, App.Logger, dispatcher);
				_repository.Init();

				OnExplicitPropertyChanged("Repository");

				SelectedNote = null;
				OnExplicitPropertyChanged("NotesView");
			}
			catch (Exception e)
			{
				App.Logger.Error("Main", "Full Synchronization failed", e);
				ExceptionDialog.Show(Owner, "Full Synchronization failed", e);
			}
		}

		private void FilterNoteList()
		{
			var sn = SelectedNote;

			_preventScintillaFocus = true;
			{
				NotesView.Refresh();
				if (NotesView.Contains(sn)) 
					SelectedNote = sn;
				else
					SelectedNote = NotesView.FirstOrDefault<INote>();
			}
			_preventScintillaFocus = false;
		}

		public void SetSelectedNoteWithoutFocus(INote n)
		{
			try
			{
				_preventScintillaFocus = true;
				SelectedNote = n;
			}
			finally
			{
				_preventScintillaFocus = false;
			}
		}

		private bool SearchFilter(INote note)
		{
			if (string.IsNullOrWhiteSpace(SearchText)) return true;

			if (IsRegex(SearchText, out var searchRegex))
			{
				if (searchRegex.IsMatch(note.Title)) return true;
				if (searchRegex.IsMatch(note.Text)) return true;
				if (note.Tags.Any(t => searchRegex.IsMatch(t))) return true;

				return false;
			}
			else if (SearchText.Length > 2 && SearchText.StartsWith("[") && SearchText.EndsWith("]"))
			{
				var searchTag = SearchText.Substring(1, SearchText.Length - 2);

				if (note.HasTagCasInsensitive(searchTag)) return true;

				return false;
			}
			else
			{
				if (note.Title.ToLower().Contains(SearchText.ToLower())) return true;
				if (note.Text.ToLower().Contains(SearchText.ToLower())) return true;
				if (note.HasTagCasInsensitive(SearchText)) return true;

				return false;
			}
		}

		private bool IsRegex(string text, out Regex r)
		{
			try
			{
				if (text.Length >= 3 && text.StartsWith("/") && text.EndsWith("/"))
				{
					r = new Regex(text.Substring(1, text.Length - 2));
					return true;
				}
				else
				{
					r = null;
					return false;
				}

			}
			catch (ArgumentException)
			{
				r = null;
				return false;
			}
		}

		private void SaveSettings()
		{
			Settings.Save();
		}

		public void RequestSettingsSave()
		{
			_invSaveSettings.Request();
		}

		private void ManuallyCheckForUpdates()
		{
			try
			{
				var ghc = new GithubConnection(App.Logger);
				var r = ghc.GetLatestRelease();

				if (r.Item1 > App.APP_VERSION)
				{
					UpdateWindow.Show(Owner, this, r.Item1, r.Item2, r.Item3);
				}
				else
				{
					MessageBox.Show("You are using the latest version");
				}
			}
			catch (Exception e)
			{
				App.Logger.Error("Main", "Can't get latest version from github", e);
				MessageBox.Show("Cannot get latest version from github API");
			}
		}

		private void CheckForUpdatesAsync()
		{
			try
			{
				var ghc = new GithubConnection(App.Logger);
				var r = ghc.GetLatestRelease();

				if (r.Item1 > App.APP_VERSION)
				{
					Application.Current.Dispatcher.BeginInvoke(new Action(() => { UpdateWindow.Show(Owner, this, r.Item1, r.Item2, r.Item3); }));
				}
			}
			catch (Exception e)
			{
				App.Logger.Error("Main", "Can't get latest version from github", e);
			}
		}

		private void ShowDocSearchBar()
		{
			Owner.ShowDocSearchBar();
		}

		private void HideDocSearchBar()
		{
			Owner.HideDocSearchBar();
		}

		private void DebugCreateIpsumNotes()
		{
			for (int i = 0; i < 10; i++)
			{
				string title = CreateLoremIpsum(4 + App.GlobalRandom.Next(5), 16);
				string text = CreateLoremIpsum((16 + App.GlobalRandom.Next(16)) * (8 + App.GlobalRandom.Next(8)), App.GlobalRandom.Next(8)+8);

				var n = Repository.CreateNewNote();

				n.Title = title;
				n.Text = text;

				int tc = App.GlobalRandom.Next(5);
				for (int j = 0; j < tc; j++) n.Tags.Add(CreateLoremIpsum(1,1));
			}
		}

		private string CreateLoremIpsum(int len, int linelen)
		{
			var words = Regex.Split(Properties.Resources.LoremIpsum, @"\r?\n");
			StringBuilder b = new StringBuilder();
			for (int i = 0; i < len; i++)
			{
				if (i>0 && i % linelen == 0) b.Append("\r\n");
				else if (i > 0) b.Append(" ");
 
				b.Append(words[App.GlobalRandom.Next(words.Length)]);
			}
			return b.ToString(0,1).ToUpper() + b.ToString().Substring(1);
		}

		private void ChangeSettingAlwaysOnTop()
		{
			Settings.AlwaysOnTop = !Settings.AlwaysOnTop;

			ChangeSettings(Settings);
		}
		
		private void ChangeSettingLineNumbers()
		{
			Settings.SciLineNumbers = !Settings.SciLineNumbers;

			ChangeSettings(Settings);
		}

		private void ChangeSettingWordWrap()
		{
			Settings.SciWordWrap = !Settings.SciWordWrap;

			ChangeSettings(Settings);
		}

		public void OnNewNoteDrop(IDataObject data)
		{
			try
			{
				if (data.GetDataPresent(DataFormats.FileDrop, true))
				{
					string[] paths = data.GetData(DataFormats.FileDrop, true) as string[];
					foreach (var path in paths ?? new string[0])
					{
						var filename = Path.GetFileNameWithoutExtension(path) ?? "New note from unknown file";
						var filecontent = File.ReadAllText(path);

						SelectedNote = Repository.CreateNewNote();
						SelectedNote.Title = filename;
						SelectedNote.Text  = filecontent;
					}
				}
				else if (data.GetDataPresent(DataFormats.Text, true))
				{
					var notetitle   = "New note from drag&drop";
					var notecontent = data.GetData(DataFormats.Text, true) as string;
					if (!string.IsNullOrWhiteSpace(notecontent))
					{
						SelectedNote = Repository.CreateNewNote();
						SelectedNote.Title = notetitle;
						SelectedNote.Text  = notecontent;
					}
				}
			}
			catch (Exception ex)
			{
				ExceptionDialog.Show(Owner, "Drag&Drop failed", "Drag and Drop operation failed due to an internal error", ex);
			}
		}

		private void CreateNoteFromClipboard()
		{
			if (Clipboard.ContainsFileDropList())
			{
				if (Owner.Visibility == Visibility.Hidden) ShowMainWindow();

				foreach (var path in Clipboard.GetFileDropList())
				{
					var filename = Path.GetFileNameWithoutExtension(path) ?? "New note from unknown file";
					var filecontent = File.ReadAllText(path);

					SelectedNote = Repository.CreateNewNote();
					SelectedNote.Title = filename;
					SelectedNote.Text = filecontent;
				}
			}
			else if (Clipboard.ContainsText())
			{
				if (Owner.Visibility == Visibility.Hidden) ShowMainWindow();

				var notetitle = "New note from clipboard";
				var notecontent = Clipboard.GetText();
				if (!string.IsNullOrWhiteSpace(notecontent))
				{
					SelectedNote = Repository.CreateNewNote();
					SelectedNote.Title = notetitle;
					SelectedNote.Text = notecontent;
				}
			}
		}

		private void InsertSnippet(string snip)
		{
			if (SelectedNote == null) return;
			
			snip = _spsParser.Parse(snip, out bool succ);

			if (!succ)
			{
				App.Logger.Warn("Main", "Snippet has invalid format: '" + snip + "'");
			}

			Owner.NoteEdit.ReplaceSelection(snip);

			Owner.FocusScintilla();
		}
	}
}
