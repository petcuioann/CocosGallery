using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Windows.Storage;

namespace CocosGallery {
    /// <summary>Represents a folder location in the gallery.</summary>
    public class FolderItem : INotifyPropertyChanged {
        private string _name = string.Empty;
        private string _path = string.Empty;

        /// <summary>The display name of the folder.</summary>
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        /// <summary>The absolute path of the folder.</summary>
        public string Path { get => _path; set { _path = value; OnPropertyChanged(); } }
        /// <summary>True if this is a system folder that cannot be edited.</summary>
        public bool IsSpecial { get; set; } = false;

        public Visibility IsEditableVisibility => IsSpecial ? Visibility.Collapsed : Visibility.Visible;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>Represents a single media item (image or video).</summary>
    public class MediaItem : INotifyPropertyChanged {
        /// <summary>The file name of the media.</summary>
        public string Title { get; set; } = string.Empty;
        /// <summary>The loaded thumbnail image.</summary>
        public BitmapImage Thumbnail { get; set; } = null!;
        /// <summary>Indicates if the file is a video.</summary>
        public bool IsVideo { get; set; }
        /// <summary>The absolute path to the file.</summary>
        public string FilePath { get; set; } = string.Empty;

        public StorageFile? FileRecord { get; set; }

        public string Extension => System.IO.Path.GetExtension(FilePath).ToLower();

        private bool _isFavorite;
        public bool IsFavorite { get => _isFavorite; set { if (_isFavorite != value) { _isFavorite = value; OnPropertyChanged(); } } }

        private bool _isDeleted;
        public bool IsDeleted { get => _isDeleted; set { if (_isDeleted != value) { _isDeleted = value; OnPropertyChanged(); } } }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectionIndicatorVisibility)); } } }

        private int _selectionIndex;
        public int SelectionIndex { get => _selectionIndex; set { if (_selectionIndex != value) { _selectionIndex = value; OnPropertyChanged(); } } }

        private bool _isSelectModeActive;
        public bool IsSelectModeActive { get => _isSelectModeActive; set { if (_isSelectModeActive != value) { _isSelectModeActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(FocusButtonVisibility)); } } }

        public Visibility SelectionIndicatorVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FocusButtonVisibility => IsSelectModeActive ? Visibility.Visible : Visibility.Collapsed;
        public HashSet<string> Tags { get; } = new();
        public Visibility IsVideoVisibility => this.IsVideo ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Constructs a new media item.</summary>
        public MediaItem(string title, BitmapImage thumbnail, bool isVideo, string filePath, StorageFile? fileRecord = null) { this.Title = title; this.Thumbnail = thumbnail; this.IsVideo = isVideo; this.FilePath = filePath; this.FileRecord = fileRecord; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
