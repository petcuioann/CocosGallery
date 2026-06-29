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

    public class MediaItem : INotifyPropertyChanged {
        private BitmapImage? _thumbnail;
        private bool _isFavorite;
        private bool _isDeleted;
        private bool _isSelected;
        private int _selectionIndex;
        private bool _isSelectModeActive;

        public string Title { get; set; } = string.Empty;
        public BitmapImage? Thumbnail { get => _thumbnail; set { if (_thumbnail != value) { _thumbnail = value; OnPropertyChanged(); } } }
        public bool IsVideo { get; set; }
        public string FilePath { get; set; } = string.Empty;

        public string Extension => System.IO.Path.GetExtension(FilePath).ToLower();

        public bool IsFavorite { get => _isFavorite; set { if (_isFavorite != value) { _isFavorite = value; OnPropertyChanged(); } } }
        public bool IsDeleted { get => _isDeleted; set { if (_isDeleted != value) { _isDeleted = value; OnPropertyChanged(); } } }
        public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectionIndicatorVisibility)); } } }
        public int SelectionIndex { get => _selectionIndex; set { if (_selectionIndex != value) { _selectionIndex = value; OnPropertyChanged(); } } }
        public bool IsSelectModeActive { get => _isSelectModeActive; set { if (_isSelectModeActive != value) { _isSelectModeActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(FocusButtonVisibility)); } } }

        public Visibility SelectionIndicatorVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FocusButtonVisibility => IsSelectModeActive ? Visibility.Visible : Visibility.Collapsed;
        public HashSet<string> Tags { get; } = new();
        public Visibility IsVideoVisibility => IsVideo ? Visibility.Visible : Visibility.Collapsed;

        public MediaItem(string title, BitmapImage? thumbnail, bool isVideo, string filePath) {
            Title = title; Thumbnail = thumbnail; IsVideo = isVideo; FilePath = filePath;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
