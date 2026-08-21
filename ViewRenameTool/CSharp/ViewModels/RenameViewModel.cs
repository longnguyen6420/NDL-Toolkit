using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace ViewRenameTool.ViewModels
{
    public class ViewRenameItem : INotifyPropertyChanged
    {
        public View RevitView { get; }
        public ElementId ViewId => RevitView.Id;
        public string OriginalName => RevitView.Name;
        public string LevelName { get; }

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _calculatedName = string.Empty;
        public string CalculatedName
        {
            get => _calculatedName;
            set
            {
                if (_calculatedName != value)
                {
                    _calculatedName = value;
                    OnPropertyChanged();
                }
            }
        }

        public ViewRenameItem(View view, string levelName, bool isSelected = true)
        {
            RevitView = view;
            LevelName = levelName;
            IsSelected = isSelected;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RenameViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<ViewRenameItem> Items { get; }

        private string _baseName = "VERTICAL PENETRATION SLEEVE";
        public string BaseName
        {
            get => _baseName;
            set
            {
                _baseName = value?.ToUpper() ?? string.Empty;
                OnPropertyChanged();
                UpdatePreview();
            }
        }

        private bool _useLevelPrefix = true;
        public bool UseLevelPrefix
        {
            get => _useLevelPrefix;
            set
            {
                _useLevelPrefix = value;
                OnPropertyChanged();
                UpdatePreview();
            }
        }

        private string _customPrefix = string.Empty;
        public string CustomPrefix
        {
            get => _customPrefix;
            set
            {
                _customPrefix = value?.ToUpper() ?? string.Empty;
                OnPropertyChanged();
                UpdatePreview();
            }
        }

        private string _suffix = string.Empty;
        public string Suffix
        {
            get => _suffix;
            set
            {
                _suffix = value?.ToUpper() ?? string.Empty;
                OnPropertyChanged();
                UpdatePreview();
            }
        }

        private string _separator = " ";
        public string Separator
        {
            get => _separator;
            set
            {
                _separator = value;
                OnPropertyChanged();
                UpdatePreview();
            }
        }

        private bool _autoIndex = true;
        public bool AutoIndex
        {
            get => _autoIndex;
            set
            {
                _autoIndex = value;
                OnPropertyChanged();
                UpdatePreview();
            }
        }

        public RenameViewModel(List<ViewRenameItem> items)
        {
            Items = new ObservableCollection<ViewRenameItem>(items);
            UpdatePreview();
        }

        public void UpdatePreview()
        {
            var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in Items)
            {
                if (!item.IsSelected)
                {
                    item.CalculatedName = item.OriginalName.ToUpper();
                    continue;
                }

                var prefixParts = new List<string>();
                if (UseLevelPrefix && !string.IsNullOrWhiteSpace(item.LevelName))
                {
                    prefixParts.Add(item.LevelName.Trim().ToUpper());
                }
                if (!string.IsNullOrWhiteSpace(CustomPrefix))
                {
                    prefixParts.Add(CustomPrefix.Trim().ToUpper());
                }

                string prefixStr = string.Join(Separator, prefixParts).ToUpper();
                var fullParts = new List<string>();

                if (!string.IsNullOrEmpty(prefixStr))
                    fullParts.Add(prefixStr);

                if (!string.IsNullOrWhiteSpace(BaseName))
                    fullParts.Add(BaseName.Trim().ToUpper());
                else if (fullParts.Count == 0 && string.IsNullOrWhiteSpace(Suffix))
                    fullParts.Add(item.OriginalName.ToUpper());

                string combined = string.Join(Separator, fullParts).ToUpper();

                if (!string.IsNullOrWhiteSpace(Suffix))
                {
                    combined = string.IsNullOrEmpty(combined) ? Suffix.Trim().ToUpper() : (combined + Separator + Suffix.Trim()).ToUpper();
                }

                string calculated;
                if (nameCounts.TryGetValue(combined, out int count))
                {
                    nameCounts[combined] = count + 1;
                    calculated = AutoIndex ? $"{combined}{Separator}{(count + 1):D2}" : combined;
                }
                else
                {
                    nameCounts[combined] = 1;
                    calculated = combined;
                }

                item.CalculatedName = calculated.ToUpper();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
