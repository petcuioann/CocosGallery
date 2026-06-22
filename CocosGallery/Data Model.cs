using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Windows.Storage;

namespace CocosGallery
{
    public class FolderItem : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _path = string.Empty;

        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        public string Path { get => _path; set { _path = value; OnPropertyChanged(); } }
        public bool IsSpecial { get; set; } = false;

        public Visibility IsEditableVisibility => IsSpecial ? Visibility.Collapsed : Visibility.Visible;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class MediaItem : INotifyPropertyChanged
    {
        public string Title { get; set; } = string.Empty;
        public BitmapImage Thumbnail { get; set; } = null!;
        public bool IsVideo { get; set; }
        public string FilePath { get; set; } = string.Empty;

        public StorageFile? FileRecord { get; set; }

        public string Extension => System.IO.Path.GetExtension(FilePath).ToLower();

        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set { if (_isFavorite != value) { _isFavorite = value; OnPropertyChanged(); } }
        }

        private bool _isDeleted;
        public bool IsDeleted
        {
            get => _isDeleted;
            set { if (_isDeleted != value) { _isDeleted = value; OnPropertyChanged(); } }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectionIndicatorVisibility));
                }
            }
        }

        private int _selectionIndex;
        public int SelectionIndex
        {
            get => _selectionIndex;
            set { if (_selectionIndex != value) { _selectionIndex = value; OnPropertyChanged(); } }
        }

        private bool _isSelectModeActive;
        public bool IsSelectModeActive
        {
            get => _isSelectModeActive;
            set
            {
                if (_isSelectModeActive != value)
                {
                    _isSelectModeActive = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FocusButtonVisibility));
                }
            }
        }

        public Visibility SelectionIndicatorVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FocusButtonVisibility => IsSelectModeActive ? Visibility.Visible : Visibility.Collapsed;
        public HashSet<string> Tags { get; } = new();
        public Visibility IsVideoVisibility => this.IsVideo ? Visibility.Visible : Visibility.Collapsed;

        public MediaItem(string title, BitmapImage thumbnail, bool isVideo, string filePath, StorageFile? fileRecord = null)
        {
            this.Title = title;
            this.Thumbnail = thumbnail;
            this.IsVideo = isVideo;
            this.FilePath = filePath;
            this.FileRecord = fileRecord;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
